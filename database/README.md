# Database

**Default: SQL Server with stored procedures** (QPS style) — see **[`sqlserver/README.md`](sqlserver/README.md)**:
scripts `sqlserver/00_create_database.sql` … `03_seed.sql`, configuration (`ConnectionStrings:DefaultConnection`,
encoded password, `PoGovSettings:Storage = sqlserver`), the migration from the .37 PostgreSQL with
`tools/PgToSqlServer` (copy + verify) and the rollback.

The SQL Server schema has the same tables and columns as the PostgreSQL schema v2 below, and the same JSON
text, so the two stores are interchangeable (`PoGovSettings:Storage`), and data can be copied both ways.

# PostgreSQL (schema v2) — fallback and migration source

With `PoGovSettings:Storage = postgres` the .NET API (`POGovApi`) uses **the same PostgreSQL schema v2 as v1.1.4** (`legacy/python-app/storage.py`,
`PgStore`). Tables, columns, document and log-record JSON are unchanged, so:

* the existing database on **.37** (`localhost:5433`, database `pogov`, user `vmm_app`) works as is;
* data written by the new API can still be read by v1.1.4 (verified: Python → .NET → Python round trip
  of logins/password hashes, uploads, requests, approvals, runs, feedback, audit).

`schema.sql` is the schema as plain SQL. You do not have to run it: the API creates any missing table
at start-up (`CREATE ... IF NOT EXISTS`, `PgStore.InitializeAsync`), exactly like `server.py` did.

## Point the API at the existing .37 database

1. Stop the v1.1.4 service first. **Do not run v1.1.4 and the new API against the same database at the
   same time** — each keeps users/sessions in memory and serialises writes with an in-process lock.
2. Take a backup (v1.1.4 *Admin → Backups → Back up now*, or `pg_dump -p 5433 -U vmm_app pogov > pogov.sql`).
3. Put the connection in `POGovApi/appsettings.Production.json` (git-ignored) or in an environment
   variable `ConnectionStrings__PoGov`, and select the PostgreSQL store:

   ```json
   {
     "ConnectionStrings": { "PoGov": "Host=127.0.0.1;Port=5433;Database=pogov;Username=vmm_app;Password=<from config.json db.password>" },
     "PoGovSettings": { "Storage": "postgres", "AdminEmail": "kosaraju.srihari@vishalwholesale.co.in", "BackupDir": "D:\\vmm\\backups" }
   }
   ```

   Copy the other values of v1.1.4 `config.json` into `PoGovSettings` (same names in PascalCase:
   `admin_email` → `AdminEmail`, `restrict_to_own_mcs` → `RestrictToOwnMcs`, `anthropic_api_key` →
   `AnthropicApiKey`, `outbound_proxy` → `OutboundProxy`, …). `BusinessTimeZone` (default `Asia/Kolkata`)
   replaces "the VM's local time" for dates, IDs and timestamps.
4. Start the API. The log shows `storage: PostgreSQL vmm_app@127.0.0.1:5433/pogov, users: N`. Existing
   passwords keep working (same PBKDF2 hashes); sessions of v1.1.4 (cookies) are not used by the new
   portal — everyone signs in again.

Rollback: stop the API, start v1.1.4 — nothing in the schema changed.

## Create a new database

```sql
-- as the postgres superuser
CREATE USER vmm_app PASSWORD '<password>';
CREATE DATABASE pogov OWNER vmm_app ENCODING 'UTF8';
```

Then start the API with that connection string. It creates the tables (or run `psql -U vmm_app -d pogov
-f database/schema.sql` first), seeds `PoGovSettings:AdminEmail` as Admin + Planner with the default password
(`1234`, must be changed at first sign-in). To bring data over from a v1.1.4 *files* installation, use
v1.1.4's `migrate_to_db.py` (or unzip a `vmm-data-*.zip` backup into a data folder and run it on that).

## Backups

`PO_BACKUP_CREATE` (`POST api/v1/BackupCreate`) and the nightly job (after `BackupHour`, kept `BackupKeepDays`) write
`vmm-data-yyyyMMdd-HHmmss.zip` to `BackupDir` in the **v1.1.4 file layout**
(`manifest.json`, `users.json`, `datasets/<key>/<version>.json.gz`, `requests/<day>.jsonl`, `audit.log`, …),
so a backup can be restored with the old tools (`migrate_to_db.py`) or used as a v1.1.4 data folder.

## What is where

| Table | Holds |
|---|---|
| `app_settings` | documents `manifest`, `params`, `scr_mapping` (one JSON row each) |
| `app_users`, `app_sessions` | documents `users` / `sessions`, one row per key (only changed rows are written) |
| `doc_stamps` | change counter per document (users are reloaded when another process changed them) |
| `dataset_versions` | every uploaded data-file version, gzip `{"rows":[…],"meta":{…}}` |
| `upload_log`, `upload_batches` | upload history and Upload IDs (`UPL-yyyyMMdd-0001`) |
| `request_events` | Tool 1 events per day (`request` / `approval` / `clear`), replayed to get the day's state; IDs `REQ-yyyyMMdd-0001` |
| `run_index`, `run_files` | saved Tool 2 runs (`RUN-yyyyMMdd-0001`), deleted runs are only flagged |
| `audit_log`, `feedback_log` | audit trail; feedback (`FB-yyyyMMdd-0001`) and status changes |
| `schema_info` | `version = 2` |

The SQL Server tables are the same (see `sqlserver/README.md` for the type mapping).

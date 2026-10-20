# Database — SQL Server (default)

The API stores everything in SQL Server through **stored procedures**, the same way the QPS solution does:
`Infrastructure/Utilitys/DapperHelper.cs` (QPS method names, Dapper + Microsoft.Data.SqlClient) calls the
`PRO_POGOV_*` procedures with `DynamicParameters`, a `@Cmd` switch where one procedure serves several reads, and
`@Error_Code INT OUTPUT` / `@Error_Message NVARCHAR(500) OUTPUT` (0 / '' = success). The store is
`Infrastructure/Repositories/SqlServerStore.cs`; it behaves exactly like the PostgreSQL store (same tables, same
JSON text, same IDs), so data moves both ways with the migration tool.

| Script | Run in | Does |
|---|---|---|
| `00_create_database.sql` | `master` | creates database `POGovernance` (if missing), turns on `READ_COMMITTED_SNAPSHOT`; commented example for a `vmm_app` SQL login |
| `01_tables.sql` | `POGovernance` | the 13 tables of schema v2 + index `request_events_day (req_date, id)` |
| `02_procedures.sql` | `POGovernance` | the 14 `PRO_POGOV_*` procedures (`CREATE OR ALTER`) |
| `03_seed.sql` | `POGovernance` | `schema_info`: `version = 2` |

All four are idempotent — run them again after an upgrade.

## 1. Create the database

```bat
sqlcmd -S <server> -E -i 00_create_database.sql
sqlcmd -S <server> -E -d POGovernance -i 01_tables.sql -i 02_procedures.sql -i 03_seed.sql
```

(`-E` = Windows login; with a SQL login use `-U sa -P <pw>`; add `-C` to trust a self-signed server certificate.)
In SSMS: open each file, choose the database (`master` for 00, `POGovernance` for the others), Execute.

The API needs only `EXECUTE` on the procedures (they own the table access through ownership chaining):
`GRANT EXECUTE ON SCHEMA::dbo TO vmm_app;` (see the end of `00_create_database.sql`). The migration tool
additionally reads the tables (row counts) — run it as `db_owner` or `sa`.

Instead of running 01..03 yourself you can set `PoGovSettings:SqlServerCreateSchema = true`: the API then runs the
embedded copies of 01..03 at start-up when `schema_info` is missing (it needs `CREATE TABLE` / `CREATE PROCEDURE`
rights for that). Without it, a database without the schema stops the start-up with a message naming the scripts.

## 2. Configure the API (`POGovApi/appsettings.json`, or `appsettings.Production.json`, or environment variables)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=HOIT1001527DT;Database=POGovernance;User Id=vmm_app;Password=<My_Encode(password)>;TrustServerCertificate=True;"
  },
  "PoGovSettings": {
    "Storage": "sqlserver",
    "EncryptedDbPassword": true,
    "SqlServerCreateSchema": false
  }
}
```

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | — | SQL Server connection (QPS name). `PoGovSettings:ConnectionString` is used when it is empty |
| `PoGovSettings:Storage` | `sqlserver` | `sqlserver` or `postgres` (the v1.1.4 PostgreSQL store, `ConnectionStrings:PoGov`) |
| `PoGovSettings:EncryptedDbPassword` | `false` in code, `true` in appsettings.json | the `Password` inside `DefaultConnection` is `My_Encode`d (QPS `MyNewEncDec.dll`) and decoded with `New_Enc_Dec.My_Decode` at start-up |
| `PoGovSettings:SqlServerCreateSchema` | `false` | create the schema at start-up when missing |
| `PoGovSettings:DbWaitSeconds` | `120` | how long the start-up waits for the server |

**Encoded password.** Like QPS, the password is stored encoded with `New_Enc_Dec.My_Encode` (from
`Infrastructure/Dlls/MyNewEncDec.dll`, the QPS DLL; it is a .NET Framework 4.5 assembly and loads on .NET 8 —
covered by a unit test). Produce the encoded text with the same tool you use for QPS, or with a two-line C#
snippet against the DLL: `new MyNewEncDec.New_Enc_Dec().My_Encode("<password>")`. Windows / integrated logins
(`Trusted_Connection=True`) have no password, so nothing is decoded. Set `EncryptedDbPassword` to `false` to put a
plain password in the connection string (e.g. in a test environment variable).

With **Microsoft.Data.SqlClient** (used instead of QPS's System.Data.SqlClient, which Microsoft has deprecated —
same API, same connection strings) encryption is on by default: either the server has a certificate the VM trusts,
or add `TrustServerCertificate=True` (as above) / `Encrypt=False`.

Start the API — the log shows `storage: SQL Server vmm_app@<server>/POGovernance, users: N`. On an empty database
it creates `PoGovSettings:AdminEmail` (Admin + Planner) with the default password `1234`, to be changed at the
first sign-in.

## 3. Migrate from the .37 PostgreSQL (v1.1.4 data)

`tools/PgToSqlServer` copies everything with the logic of `storage.py copy_store` and then compares both sides with
`verify_copy` (documents, every log, every request day, the audit trail, the list and the bytes of every data-file
version and saved run). Data-file versions and runs keep their original upload time.

1. Stop the v1.1.4 service and the .NET API (nothing may write to PostgreSQL during the copy).
2. Back up PostgreSQL: `pg_dump -p 5433 -U vmm_app pogov > pogov-before-sqlserver.sql`.
3. Create the SQL Server database (section 1), or let the tool do it (`--create-database --create-schema`).
4. Copy and verify:

   ```bat
   dotnet run --project tools/PgToSqlServer -c Release -- ^
     --pg  "Host=127.0.0.1;Port=5433;Database=pogov;Username=vmm_app;Password=<pg password>" ^
     --sql "Server=<server>;Database=POGovernance;User Id=sa;Password=<pw>;TrustServerCertificate=True"
   ```

   Options: `--encrypted-password` (the `--sql` password is `My_Encode`d), `--create-database`, `--create-schema`,
   `--force` (empty a non-empty target first — without it the tool refuses to copy), `--verify-only` (compare
   only), `--reverse` (SQL Server → PostgreSQL). Connection strings can also come from `POGOV_PG` / `POGOV_SQL`.
   It prints progress, the row count of every table on both sides and ends with `OK - no differences`
   (exit code 0; 1 = differences, 2 = refused, 3 = error).
5. Point the API at SQL Server (section 2) and start it. Users sign in with their existing passwords (the PBKDF2
   hashes are copied unchanged).

Tested on the local copies of the v1.1.4 data (`pogov_sweep`, `pogov_e2e`): 12 tables, identical row counts, the
JSON text of every row byte-identical, `verify_copy` 0 differences; the API signs in on the copy with the v1.1.4
password.

## 4. Rollback

* **Before anything was written to SQL Server:** set `PoGovSettings:Storage = postgres` (connection
  `ConnectionStrings:PoGov`) and restart — PostgreSQL was not changed by the migration.
* **After users worked on SQL Server:** stop the API, copy SQL Server back into PostgreSQL and verify:
  `PgToSqlServer --reverse --force --pg "…" --sql "…"` (empties the PostgreSQL tables first — take a `pg_dump`
  before), then set `Storage = postgres` (or start v1.1.4, which reads the same schema) and restart.

## How it works

| Procedure | `@Cmd` | IStore operation |
|---|---|---|
| `PRO_POGOV_DOC_GET` | `GET_DOC`, `GET_USERS`, `GET_SESSIONS`, `GET_STAMP` | GetDoc, DocStamp (`@Stamp OUTPUT`) |
| `PRO_POGOV_DOC_PUT` | — | PutDoc of manifest / params / scr_mapping (+ stamp, one transaction) |
| `PRO_POGOV_DOC_MAP_PUT` | `USERS`, `SESSIONS` | PutDoc of users / sessions: only the changed rows (`@Rows` JSON array, `MERGE`) and deleted keys (`@Deleted`), stamp checked (`@Expected_Stamp`) and bumped, one transaction |
| `PRO_POGOV_LOG_READ` | `READ`, `READ_DAYS`, `DAYS` | ReadLog, ReadLogs (`@Days` JSON array), LogDays |
| `PRO_POGOV_LOG_APPEND` | — | AppendLog |
| `PRO_POGOV_LOG_REWRITE` | — | RewriteLog (delete + insert in array order, one transaction) |
| `PRO_POGOV_LOG_DELETE_DAY` | — | DeleteLogDay |
| `PRO_POGOV_AUDIT` | `APPEND`, `APPEND_MANY`, `READ` | AuditAppend, bulk insert (migration), AuditRead |
| `PRO_POGOV_BLOB_PUT` | — | PutBlob (`@Created_At` only for the migration) |
| `PRO_POGOV_BLOB_GET` | `GET`, `GET_ANY`, `SIZE`, `LIST` | GetBlob, GetBlobAny, BlobSize / BlobExists, ListBlobs |
| `PRO_POGOV_BLOB_DELETE` | `MARK_DELETED`, `DELETE` | MarkBlobDeleted, DeleteBlob |
| `PRO_POGOV_STORE_INFO` | `STORAGE_BYTES`, `IS_EMPTY`, `SCHEMA_VERSION` | StorageBytes, IsEmpty, start-up check |
| `PRO_POGOV_TRUNCATE_ALL` | (`@Confirm = 'TRUNCATE ALL'`) | empty all data tables (migration `--force`) |
| `PRO_POGOV_APP_LOCK` | `GET`, `RELEASE`, `STATE` | LockAsync (the server-wide write lock) |

* **Types:** PostgreSQL `json` → `NVARCHAR(MAX)` with `CHECK (ISJSON(…) = 1)` (text kept exactly as the API
  writes it, Python `json.dumps` format); `bytea` → `VARBINARY(MAX)`; `timestamptz` → `DATETIMEOFFSET(7)`
  default `SYSDATETIMEOFFSET()`; `bigserial` → `BIGINT IDENTITY`. Key columns use `Latin1_General_100_BIN2`
  (case-sensitive and ordered like PostgreSQL's `C` collation), whatever the server's default collation is.
* **Write lock:** `PRO_POGOV_APP_LOCK` takes an exclusive `sp_getapplock` owned by the **session** of one
  dedicated, non-pooled connection that the API keeps open while it holds the lock. Unlike v1.1.4 / PostgreSQL
  (a lock inside one process), several API instances on one database serialise their writes. If an API process or
  its connection dies, SQL Server ends the session and releases the lock. Waiting instances log
  "Waiting for the database write lock …" every 10 s.
* **Users / sessions:** when another instance wrote the document since this instance read it, the procedure
  refuses the diff (`50409`) and the API re-reads the rows before writing.
* **Errors:** connection failures are retried once on a fresh connection (pool cleared), deadlocks up to three
  times; while the server is unreachable the API answers 503 "The database is not reachable right now".
* **Tests:** `tests/POGovernance.Tests/SqlServer` runs every store scenario (documents, users map, logs, audit,
  blobs incl. a 20 MB blob, lock exclusivity between two store instances) against a real server when the
  environment variable `POGOV_SQLSERVER_TEST` holds a connection string (each run creates and drops its own
  database); otherwise they are skipped.

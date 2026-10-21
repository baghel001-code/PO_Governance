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
| `04_api_master.sql` | `POGovernance` | QPS API master: table `API_MASTER`, procedure `PRO_GET_ALL_API` (`@Cmd` `ALL` / `BY_TASK`), one row per POGovApi portal action |
| `05_sap_interface.sql` | `POGovernance` | SAP R/3 interface (schema **v3**): tables `SAP_TECH_USERS`, `SAP_DECISION_LOG`, `SAP_DECISION_ITEMS`, `SAP_EVENTS`, `SAP_REASON_CODES` (seeded), sequence `SAP_DECISION_SEQ`, the 6 `PRO_SAP_*` procedures; `schema_info.version = 3` |

All six are idempotent — run them again after an upgrade (04 only inserts missing task names; it never overwrites a row;
05 inserts missing exception reason codes and refreshes the verdict reason codes).

## 1. Create the database

```bat
sqlcmd -S <server> -E -i 00_create_database.sql
sqlcmd -S <server> -E -d POGovernance -i 01_tables.sql -i 02_procedures.sql -i 03_seed.sql -i 04_api_master.sql -i 05_sap_interface.sql
```

**Upgrade of an existing v2 database:** run `04_api_master.sql` (new PO_SAP_* rows) and `05_sap_interface.sql`. Until 05 is run
the API starts and the portal works, but it logs "run database/sqlserver/05_sap_interface.sql" and the SAP endpoints fail
(with `SqlServerCreateSchema = true` the API runs 05 itself at start-up).

Before running `04_api_master.sql`, set `@ApiBase` at the top of its seed to the URL of POGovApi on this server
(default `http://localhost:5080/`) and `@ApiToken` to the `My_Encode`d static token (= `JwtSettings:SecretKey`).

(`-E` = Windows login; with a SQL login use `-U sa -P <pw>`; add `-C` to trust a self-signed server certificate.)
In SSMS: open each file, choose the database (`master` for 00, `POGovernance` for the others), Execute.

The API needs only `EXECUTE` on the procedures (they own the table access through ownership chaining):
`GRANT EXECUTE ON SCHEMA::dbo TO vmm_app;` (see the end of `00_create_database.sql`). The migration tool
additionally reads the tables (row counts) — run it as `db_owner` or `sa`.

Instead of running 01..05 yourself you can set `PoGovSettings:SqlServerCreateSchema = true`: the API then runs the
embedded copies of 01..05 at start-up when `schema_info` is missing, 05 alone on a v2 database, and 04 alone when only `API_MASTER` is missing
(it needs `CREATE TABLE` / `CREATE PROCEDURE` rights for that; the seed gets `ApiMaster:BaseUrl` as its base URL).
Without it, a database without the schema stops the start-up with a message naming the scripts; a database without
`API_MASTER` only logs an error (the portal cannot load its API list until 04 is run).

### The API master (04_api_master.sql)

The portal never builds API URLs: it loads the active rows of `API_MASTER` at start-up (`GetAllApi` → `PRO_GET_ALL_API`)
and calls each API by its task name (`AllApiNames.PO_TOOL1_VALIDATE` → row `PO_TOOL1_VALIDATE` →
`POST http://<api>/api/v1/Tool1Validate`, its timeout in seconds, `Authorization: Bearer <static token>`). Columns are
QPS's `ApiData`: `Api_Name`, `Task_Name` (unique), `Api_Url`, `Api_Username`, `Api_Pwd`, `Api_Proxy`, `Api_Port`
(proxy, used when the portal's `AppConfigurationSettings:ProxyValue` = 1), `Api_Http_Method` (POST; GET only for
`GetAllApi` / `Health`), `Api_Content_Type`, `Api_Authorization_Type` (`Bearer`), `Api_Authorization` (the static token,
`My_Encode`d), `Api_Timeout`, `Token_Api_Name`, `Api_Token_Expiry_Time`, `Api_Token_Status`, `Active_Status`
(0 = hidden from the portal) + `Description`, `Created_At`, `Updated_At`.

Move the API to another server / port / https — one UPDATE, then restart the portal:

```sql
UPDATE dbo.API_MASTER SET Api_Url = REPLACE(Api_Url, N'http://localhost:5080/', N'https://pogov-api.company.local/'), Updated_At = SYSDATETIME();
```

New static token: `UPDATE dbo.API_MASTER SET Api_Authorization = N'<My_Encode(token)>'` and the same text in
`JwtSettings:SecretKey` (API) and `MasterApiSettings:Api_Authorization` (portal). `GRANT EXECUTE ON SCHEMA::dbo` (below)
covers `PRO_GET_ALL_API` too.

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
| `PRO_GET_ALL_API` | `ALL`, `BY_TASK` | API master list (`APIService.GetAllApi` / `GetSpecificApiData`, not IStore) |
| `PRO_SAP_TECH_USER` | `GET`, `LIST`, `ADD`, `SET_PASSWORD`, `SET_ACTIVE`, `UNLOCK`, `LOGIN_OK`, `LOGIN_FAIL` | SAP technical users (Basic auth, lockout, last call) — `SapSqlServerRepository` |
| `PRO_SAP_DECISION_SAVE` | `NEXT_ID`, `INSERT` | decision id from `SAP_DECISION_SEQ`; log row + items (`@Items` JSON) in one transaction; a second HTTP-200 row for a request_id → 50409 (filtered unique index `UX_SAP_DECISION_LOG_Request`, no range locks) |
| `PRO_SAP_DECISION_GET` | `BY_REQUEST`, `BY_ID`, `LIST`, `STATS` | idempotency lookup (body hash + stored response), detail with items, filtered list, counts + latency percentiles |
| `PRO_SAP_EVENTS_SAVE` | — | one /events batch in one transaction: de-duplication by msg_id (Dup_Count), LATE flag by queue seq, parked events stored with their error, `Applied = 0` |
| `PRO_SAP_EVENTS_GET` | `STATUS`, `LIST`, `BY_ID` | feed status (lag, last seq, counts), filtered list, one event with payload |
| `PRO_SAP_REASON_CODES` | `LIST`, `ACTIVE`, `SAVE` | reason codes; active exception codes + version (GET /reason-codes); add / change an exception code |

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
  refuses the diff (`50409`) and the API re-reads the rows before writing. `AccountsService` also checks the users /
  sessions doc stamp before every read or change and re-reads the document when another instance wrote it; a sessions
  write applies only this instance's new / ended sessions onto the stored ones, so instances never sign out each other's
  users (a sign-in / sign-out on one instance is seen by the others on their next request).
* **Errors:** connection failures are retried once on a fresh connection (pool cleared), deadlocks up to three
  times; while the server is unreachable the API answers 503 "The database is not reachable right now".
* **SAP decisions in parallel:** `PRO_SAP_DECISION_SAVE` takes no range locks and `SAP_DECISION_ITEMS` has no foreign key
  (a multi-row FK check scans the log under S locks): 50 parallel decisions all answer in well under a second. The API never
  cancels a PRO_SAP write half-way (a cancelled command would leave its transaction open on the pooled connection).
* **Tests:** `tests/POGovernance.Tests/SqlServer` runs every store scenario (documents, users map, logs, audit,
  blobs incl. a 20 MB blob, lock exclusivity between two store instances) against a real server when the
  environment variable `POGOV_SQLSERVER_TEST` holds a connection string (each run creates and drops its own
  database); otherwise they are skipped.

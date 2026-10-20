# VMM PO Governance — .NET 8 (Blazor Server + Web API)

This is the .NET rebuild of the VMM PO Governance tool (Python + browser version v1.1.4, kept unchanged in
`legacy/python-app/` for reference). It follows the clean-architecture layout of the QPS solution:
**two web applications** — the portal (`Admin`, Blazor Server) and the API (`POGovApi`) — on top of
`Domain`, `Application` and `Infrastructure` class libraries.

The main change from v1.1.4: **the decision rules now run on the server**, not in each user's browser.
They were ported function by function, and the C# results are checked against the original JavaScript (see Testing).

## Solution layout

| Project | What it holds |
|---|---|
| `Domain` | Constants (roles, dataset keys, decision labels), entities, exceptions, `Js` helpers that reproduce JavaScript number semantics, `SheetRow` (an uploaded file row), `EngineData` (snapshot of all current data files + parameters) |
| `Application` | Interfaces (`Interfaces/V1`), DTOs and all business logic in `Services/V1`: `Tool1` (New Request decisions), `Tool2` (PO Assessment), `Recommendation`, `Analytics` (GRC back-testing, attribute analysis, insights, AI assistant, help), `Ingest` (Excel / ZIP / raw-export import), `Platform` (accounts, requests ledger, approvals, history, runs, params, feedback, audit, backups) |
| `Infrastructure` | PostgreSQL store (Dapper + Npgsql), dataset cache, password hashing, Excel reader / writers, AI client, backups, middleware, DI registration |
| `POGovApi` | REST API, `Controllers/V1/*`, routes `api/v1/...` (Swagger at `/swagger` in Development) |
| `Admin` | The portal (MudBlazor). Talks only to the API |
| `tests/POGovernance.Tests` | xUnit unit + parity tests; `tests/parity/*` are the Node harnesses that run the original JavaScript |

## Screens

| View | Screen | Route |
|---|---|---|
| Planner | 📂 Data Setup (uploads, parameters, season selection) | `/planner/data` |
| Planner | 📋 PO Assessment (Tool 2) | `/planner/assessment` |
| Planner | 📈 GRC Back Testing | `/planner/grc` |
| Planner | 🔎 Attribute Analysis | `/planner/attributes` |
| Planner | 🤖 AI Insights + Planner dashboard | `/planner/ai-insights`, `/planner/dashboard` |
| Category | ✏️ New Request (Tool 1) + 📊 Recommendation | `/category/request` |
| Category | 📄 Today's requests, 📊 Insights, 🎬 Video, ❓ How it works | `/category/requests`, `/category/insights`, `/category/video`, `/category/how-it-works` |
| Both | ✅ Approvals, 🗂 History | `/approvals`, `/history` |
| Admin | 👥 Users & Security (users, SCR import, audit, backups, feedback) | `/admin/users`, `/admin/audit`, `/admin/backups`, `/admin/feedback` |

Roles are the same as v1.1.4: admin, planner, cathead, buyer. The API enforces them; the portal only hides what a role can't use.

## Running it

Requirements: .NET 8 SDK (runtime only on the server) and **SQL Server** (default; tables + `PRO_POGOV_*` stored
procedures from `database/sqlserver/*.sql`, QPS style — see `database/sqlserver/README.md`). PostgreSQL (the v1.1.4
database on .37) still works with `PoGovSettings:Storage = postgres`; `tools/PgToSqlServer` migrates it to SQL Server.

```bash
# 0. Database (once): database/sqlserver/00_create_database.sql in master, then 01..03 in POGovernance
sqlcmd -S . -E -i database/sqlserver/00_create_database.sql
sqlcmd -S . -E -d POGovernance -i database/sqlserver/01_tables.sql -i database/sqlserver/02_procedures.sql -i database/sqlserver/03_seed.sql

# 1. API — set the connection string (appsettings.json, environment variable or user secrets)
export ConnectionStrings__DefaultConnection="Server=.;Database=POGovernance;User Id=vmm_app;Password=<My_Encode(pw)>;TrustServerCertificate=True"
dotnet run --project POGovApi --urls http://localhost:5080

# 2. Portal — points at the API
export PortalSettings__ApiBaseUrl=http://localhost:5080/
dotnet run --project Admin --urls http://localhost:5090
```

**In Visual Studio:** right-click the solution → *Configure Startup Projects…* → *Multiple startup projects* →
set **POGovApi** and **Admin** to *Start*. Both launch profiles use fixed ports: the API on `http://localhost:5080`
(`https://localhost:7080`, Swagger at `/swagger`) and the portal on `http://localhost:5090` (`https://localhost:7090`).
The portal finds the API through `PortalSettings:ApiBaseUrl` in `Admin/appsettings.json` (default `http://localhost:5080/`);
if the API runs anywhere else, change that value. "Cannot reach the server (…)" on the sign-in page means the portal got
no answer from that address — the API isn't running, or the URL is wrong. If the API is running but the database isn't
reachable, the message is "The database is not reachable right now" instead: check `ConnectionStrings:DefaultConnection`.

First sign-in: the `AdminEmail` in `POGovApi/appsettings.json` (`PoGovSettings`) with the default password `1234`; you are asked to choose
a new password. Then upload the data files on **Data Setup**. Uploading the SCR products file creates logins for buyers and category heads, as before.

**Important:** don't run the Python v1.1.4 server and this API against the same PostgreSQL database at the same time (each keeps its own write lock).
On SQL Server the write lock is a database application lock (`sp_getapplock`), so several API instances can share one database.

### Configuration

`POGovApi/appsettings.json`:
- `ConnectionStrings:DefaultConnection` — SQL Server (QPS name); its password is decoded with `MyNewEncDec` (`New_Enc_Dec.My_Decode`) when `PoGovSettings:EncryptedDbPassword` is `true`.
- `PoGovSettings:Storage` — `sqlserver` (default) or `postgres` (then `ConnectionStrings:PoGov`); `PoGovSettings:SqlServerCreateSchema` — create the SQL Server schema at start-up when missing.
- `PoGovSettings` — the old `config.json` settings: `AdminEmail`, `DefaultPassword`, `PasswordMinLength`, `SessionIdleHours`, `MaxFailedLogins`, `LockoutMinutes`, `RestrictToOwnMcs`, `BackupDir` / `BackupHour` / `BackupKeepDays`, `MaxUploadMb`, `HistoryRetentionDays`, `AnthropicApiKey` / `AnthropicModel`, `GroqApiKey` / `GroqModel`, `OutboundProxy`, `BusinessTimeZone` (Asia/Kolkata).
- `Tool1:ReducedLabel` — `false` (default) labels breaches exactly like the running v1.1.4 page; `true` uses "Recommend Reduced Qty" as described in the v1.1.4 README.

`Admin/appsettings.json`: `PortalSettings:ApiBaseUrl`, `SessionIdleHours`, `StatusPollSeconds`; `Analytics:HowToVideoPath` for the how-to video (`wwwroot/video/how-to.mp4` by default — copy the video there).

### Deployment (Windows VM / IIS)

Publish both apps (`dotnet publish POGovApi -c Release -o out/api`, same for `Admin`), host each as an IIS site
(ASP.NET Core Hosting Bundle 8) or as a Windows service, serve the portal over HTTPS with the company certificate, and keep the API
reachable from the portal only (bind it to localhost or firewall it). Nightly backups are written to `BackupDir` in the v1.1.4 zip layout.

## Testing

```bash
dotnet test
# + the SQL Server store tests (skipped otherwise; each run creates and drops its own database):
POGOV_SQLSERVER_TEST="Server=localhost,1433;User Id=sa;Password=…;TrustServerCertificate=True" dotnet test
```

Most tests are **parity tests**: a Node script in `tests/parity/<area>/` runs the original functions extracted
from `legacy/python-app/app/index.html` on a fixture dataset and writes the expected results; the xUnit tests check that the C# port
returns the same decisions, quantities, reasons, texts and Excel output. To regenerate the expected results after a deliberate rule change, run the
harness again (each harness has the command in its header comment).

## Documents

- `docs/CONVERSION-GUIDE.md` — conventions used for the port.
- `docs/ui/*.md` — screen specifications (what each screen shows and where it comes from).
- `database/README.md` — database setup and moving from v1.1.4.
- `legacy/python-app/DEVELOPER-HANDOVER.md` — the v1.1.4 handover (business rules, SAP / BW plans).

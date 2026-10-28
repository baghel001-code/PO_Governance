# VMM PO Governance — .NET 8 (Blazor Server + Web API)

This is the .NET rebuild of the VMM PO Governance tool (Python + browser version v1.1.4, kept unchanged in
`legacy/python-app/` for reference). It follows the clean-architecture layout of the QPS solution:
**two web applications** — the portal (`Admin`, Blazor Server) and the API (`POGovApi`) — on top of
`Domain`, `DecisionEngine`, `Application` and `Infrastructure` class libraries. The API also serves the **SAP R/3
integration API** (`/api/sap/v1`, SAP API specification v1.4) — see `docs/sap/README.md`.

The main change from v1.1.4: **the decision rules now run on the server**, not in each user's browser.
They were ported function by function, and the C# results are checked against the original JavaScript (see Testing).

## Solution layout

| Project | What it holds |
|---|---|
| `Domain` | Constants (roles, app constants), store entities, exceptions, records, settings — no business rules; `Entities/Sap` = the SAP contract classes (snake_case, as the YAML) and the SAP monitoring DTOs, `AppSettings/SapInterfaceSettings` |
| `DecisionEngine` | **The core business-logic layer, BCL only (no project / package references).** Every verdict algorithm as synchronous, side-effect-free engines: `Tool1` (New Request decisions), `Tool2` (PO Assessment), `Recommendation`, `Grc` (back-testing, season selection), `Analytics` (attribute analysis, insights dashboards, AI prompt context, help values); the data model (`Model`: `EngineData`, `EngineParams`, `SheetRow`, dataset keys, decision labels), JS-semantics helpers (`Common`: `Js`, `JsObjectMap` …) and the result models (`Results`); `Sap` = the SAP /decisions verdict (`SapDecisionEngine` over Tool 1, `SapVerdictMap`, input normalisation). See `DecisionEngine/README.md` |
| `Application` | Interfaces for IO (`Interfaces/V1`: dataset cache, request ledger, clock, store …), the use-case services in `Services/V1` that load data and call the engines (`Tool1`, `Tool2`, `Recommendation`, `Analytics`), `Ingest` (Excel / ZIP / raw-export import), `Platform` (accounts, requests ledger, approvals, history, runs, params, feedback, audit, backups), `Sap` (decisions, events, technical-user Basic auth, monitoring — `Interfaces/V1/Sap`, `Services/V1/Sap`), DI registration (`DIResolver`) |
| `Infrastructure` | SQL Server store (`Utilitys/DapperHelper` + `PRO_*` procedures) and PostgreSQL store, dataset cache, password hashing, Excel reader / writers, AI client, backups, middleware (`StaticTokenMiddleware`, exceptions, `SapInterfaceMiddleware`: IP allow-list / SAP error bodies), the `SapBasic` authentication scheme, `Repositories/SapSqlServerRepository` (`PRO_SAP_*`), the portal's HTTP client (`Services/V1/APICallService`, QPS), DI registration |
| `POGovApi` | REST API, `Controllers/V1/*`, QPS routes `api/v1/[Action]` (Swagger at `/swagger` in Development) — see *API conventions*; `Controllers/Sap/SapController` = the SAP-facing `/api/sap/v1/{ping, reason-codes, events, decisions}` (routes fixed by the spec, Basic auth, not in the API master) |
| `Admin` | The portal (MudBlazor). Talks only to the API, by task name through the API master (`IAPICall.CallingAPI(AllApiNames.X, req)`); uses the `DecisionEngine.Results` models as its view models |
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
| Admin | 🔌 SAP Interface (overview / feed lag, decisions with items + JSON, events, technical users, reason codes, test decision) — user menu | `/admin/sap` |

Roles are the same as v1.1.4: admin, planner, cathead, buyer. The API enforces them; the portal only hides what a role can't use.

## Running it

Requirements: .NET 8 SDK (runtime only on the server) and **SQL Server** (default; tables + `PRO_POGOV_*` stored
procedures from `database/sqlserver/*.sql`, QPS style — see `database/sqlserver/README.md`). PostgreSQL (the v1.1.4
database on .37) still works with `PoGovSettings:Storage = postgres`; `tools/PgToSqlServer` migrates it to SQL Server.

```bash
# 0. Database (once): database/sqlserver/00_create_database.sql in master, then 01..05 in POGovernance
#    (04_api_master.sql = the API master list; set @ApiEnv / @ApiBase in it to this server's environment and API URL first;
#     05_sap_interface.sql = the SAP R/3 interface tables + PRO_SAP_* procedures, schema v3)
sqlcmd -S . -E -i database/sqlserver/00_create_database.sql
sqlcmd -S . -E -d POGovernance -i database/sqlserver/01_tables.sql -i database/sqlserver/02_procedures.sql -i database/sqlserver/03_seed.sql -i database/sqlserver/04_api_master.sql -i database/sqlserver/05_sap_interface.sql

# 1. API — set the connection string (appsettings.json, environment variable or user secrets)
export ConnectionStrings__DefaultConnection="Server=.;Database=POGovernance;User Id=vmm_app;Password=<My_Encode(pw)>;TrustServerCertificate=True"
dotnet run --project POGovApi --urls http://localhost:5080

# 2. Portal — finds the API through MasterApiSettings:Api_Url (GetAllApi), then the API_MASTER rows
export MasterApiSettings__Api_Url=http://localhost:5080/api/v1/GetAllApi
dotnet run --project Admin --urls http://localhost:5090
```

**In Visual Studio:** right-click the solution → *Configure Startup Projects…* → *Multiple startup projects* →
set **POGovApi** and **Admin** to *Start*. Both launch profiles use fixed ports: the API on `http://localhost:5080`
(`https://localhost:7080`, Swagger at `/swagger`) and the portal on `http://localhost:5090` (`https://localhost:7090`).
The portal finds the API through `MasterApiSettings:Api_Url` in `Admin/appsettings.json` (default
`http://localhost:5080/api/v1/GetAllApi`; `Admin/appsettings.Staging.json` for staging) and then uses the URLs of the
`API_MASTER` rows: path of the row + base URL of the API's environment from `API_BASE_URL` (Development = localhost,
Staging, Production — see *Base URL per environment* in `database/sqlserver/README.md`). "Cannot reach the server (…)" on the
sign-in page means the portal got no answer from that address — the API isn't running, or the URL is wrong (the portal log
says "Could not load the API master list …" at start-up). If the API is running but the database isn't reachable, the
message is "The database is not reachable right now" instead: check `ConnectionStrings:DefaultConnection`.

First sign-in: the `AdminEmail` in `POGovApi/appsettings.json` (`PoGovSettings`) with the default password `1234`; you are asked to choose
a new password. Then upload the data files on **Data Setup**. Uploading the SCR products file creates logins for buyers and category heads, as before.

**Important:** don't run the Python v1.1.4 server and this API against the same PostgreSQL database at the same time (each keeps its own write lock).
On SQL Server the write lock is a database application lock (`sp_getapplock`), so several API instances can share one database.

### Configuration

`POGovApi/appsettings.json`:
- `JwtSettings:SecretKey` — the static API token, `My_Encode`d (QPS); the same text goes into `MasterApiSettings:Api_Authorization` (portal) and `API_MASTER.Api_Authorization`. Change it for production (`StaticTokenMiddleware.EncodeToken("<new token>")`). `Cors:AllowBlazorApp` — browser origins allowed to call the API. `ApiMaster:BaseUrl` — the base URL written into API_BASE_URL (for the API's environment) when the API creates the schema; `ApiMaster:Environment` — overrides `ASPNETCORE_ENVIRONMENT` for choosing the API_BASE_URL / API_AUTHORIZATION rows (token per API per environment, see `database/sqlserver/README.md`).
- `ConnectionStrings:DefaultConnection` — SQL Server (QPS name); its password is decoded with `MyNewEncDec` (`New_Enc_Dec.My_Decode`) when `PoGovSettings:EncryptedDbPassword` is `true`.
- `PoGovSettings:Storage` — `sqlserver` (default) or `postgres` (then `ConnectionStrings:PoGov`); `PoGovSettings:SqlServerCreateSchema` — create the SQL Server schema at start-up when missing.
- `PoGovSettings` — the old `config.json` settings: `AdminEmail`, `DefaultPassword`, `PasswordMinLength`, `SessionIdleHours`, `MaxFailedLogins`, `LockoutMinutes`, `RestrictToOwnMcs`, `BackupDir` / `BackupHour` / `BackupKeepDays`, `MaxUploadMb`, `HistoryRetentionDays`, `AnthropicApiKey` / `AnthropicModel`, `GroqApiKey` / `GroqModel`, `OutboundProxy`, `BusinessTimeZone` (Asia/Kolkata).
- `SapInterface` — the SAP R/3 interface (`/api/sap/v1`): `Enabled`, `Environment`, `EnforceIpAllowList` (optional IP check, default `false` =
  technical user + password only) and `AllowedIps` (required when the check is on: CIDR list of the SAP application servers), `TrustedProxies`, `OnlyOnPort`, `RulesVersion`, `DecisionTimeoutMs` (2500),
  `EventsLagLimitSeconds` (60), `DefaultMode`, `BlockVerdict` (`HOLD` | `BLOCK`), `UnknownItemPolicy` (`REJECT` | `WATCH`),
  `BmHead`, lockout / password length / batch limits, `VerdictLink` (the `view_url` of a decision: `Enabled`, `ValidDays`, `BaseUrlKey` = `POGOV_PORTAL` in API_BASE_URL, optional `PortalBaseUrl`). HTTPS on 8443: `_KestrelHttpsExample` (rename to `Kestrel` in production).
  Technical users are managed on `/admin/sap`. Full description: `docs/sap/README.md`.
- `Tool1:ReducedLabel` — `false` (default) labels breaches exactly like the running v1.1.4 page; `true` uses "Recommend Reduced Qty" as described in the v1.1.4 README.

`Admin/appsettings.json`: `PortalSettings:UseHttps` (`false` for plain `http://host:port`; `true` once the portal is served over https — Secure cookie, HSTS, http→https redirect), `MasterApiSettings` (GetAllApi URL + encoded static token), `ApiNamesSettings` (task names),
`AppConfigurationSettings:ProxyValue` (1 = use the API_MASTER proxy columns) / `UserRevalidateInSeconds`, `PortalSettings:SessionIdleHours`, `StatusPollSeconds`; `Analytics:HowToVideoPath` for the how-to video (`wwwroot/video/how-to.mp4` by default — copy the video there).

### Deployment (Windows VM / IIS)

Publish both apps (`dotnet publish POGovApi -c Release -o out/api`, same for `Admin`), host each as an IIS site
(ASP.NET Core Hosting Bundle 8) or as a Windows service, serve the portal over HTTPS with the company certificate, and keep the API
reachable from the portal only (bind it to localhost or firewall it). Nightly backups are written to `BackupDir` in the v1.1.4 zip layout.

## API conventions (QPS)

The portal and the API talk exactly like QPS (the only exception is the SAP-facing `/api/sap/v1/*`, whose paths, snake_case
JSON, Basic authentication and error body are fixed by the SAP API specification — `docs/sap/README.md`; StaticTokenMiddleware,
X-Session-Token and the API master do not apply there):

* **Routes:** every controller is `[Route("api/v1/[Action]")]` with `[ApiController]`; each action is a `POST` taking a
  typed request object (ids, dates, filters, version… are properties — no route or query-string parameters) and returns
  `Ok(response)` where the response derives from `Domain.Entities.Base.BaseResponse` (`responseCode`, `responseMessage`,
  `recordId`). Data comes in `ApiDataResponse<T>.Data`; `GET` is used only by `GetAllApi` and `Health`. JSON property names
  are the C# names (`PropertyNamingPolicy = null`, as in QPS); the DTOs keep their v1.1.4 camelCase names through
  `[JsonPropertyName]`. Uploads stay `multipart/form-data` (files in `files`, inputs as fields: `IngestFile` takes `Key` /
  `Append`, `DataPut` takes `Key` / `Rows` / `UploadId` / `Label`, `RunSave` takes `Meta`); exports stay binary
  (Excel / CSV / zip, toast text in `X-Export-Message` / `X-Message`).
* **Errors:** `responseCode` = the HTTP status (400, 401, 403, 404, 409, 503…), `responseMessage` = the v1.1.4 text,
  `Data` = the extra payload (e.g. 409 `{conflict, byOthers, seq}`, 403 `{mustChange:true}`). The portal adds its own
  codes: 1000 task name not in the API master, 1001 HTML/XML answer, 1002 empty answer, 1003 API not reachable / timeout,
  1004 any other error.
* **API master:** the portal never builds URLs. At start-up `Admin/Program.cs` calls
  `CallingAPI<ApiMasterRes, …>(AllApiNames.MasterApi, …)` = `GetAllApi` (`MasterApiSettings` in `Admin/appsettings.json`) and
  keeps the rows of `API_MASTER` (`database/sqlserver/04_api_master.sql`, procedure `PRO_GET_ALL_API`) in `AllApi.ApiDetails`.
  If the API is down then, the portal logs it and loads the list on the first call. Every call is
  `ApiMasterService.CallingAPI<ApiDataResponse<T>, TReq>(AllApiNames.PO_…, request)` (`Application/Interfaces/V1/IAPICall.cs`,
  `Infrastructure/Services/V1/APICallService.cs`), plus `CallingAPIUpload` (multipart) and `CallingAPIDownload` (file bytes,
  name, type, X-* headers). The row gives URL, method, content type, timeout (seconds) and, with
  `AppConfigurationSettings:ProxyValue = 1`, the proxy (`Api_Proxy:Api_Port`, `Api_Username` / `Api_Pwd`).
* **Two tokens:** `Authorization: Bearer <static API token>` identifies the portal — `StaticTokenMiddleware` checks it on
  every `api/v1/*` call except `Health` against `JwtSettings:SecretKey` decoded with MyNewEncDec (the same encoded text is
  in `MasterApiSettings:Api_Authorization` and `API_MASTER.Api_Authorization`; the portal decodes it). The signed-in user is
  identified by `X-Session-Token: <session token>` (from `AuthLogin`, kept in the portal's auth cookie claims and read by
  `ISessionTokenAccessor`): `SessionTokenAuthenticationHandler` turns it into the user and roles, so every role check stays
  on the server (401 "Please log in", 403 "Not allowed" / the action's own message, 403 `{mustChange}` until the password is
  changed). The portal re-checks the session (`PO_AUTH_ME`) every `AppConfigurationSettings:UserRevalidateInSeconds`.
* **CORS / origin:** policy `AllowBlazorApp` from `Cors:AllowBlazorApp` (`POGovApi/appsettings.json`); a browser request
  with an `Origin` outside that list gets 403 "Origin not allowed" (the portal calls the API from its server, without `Origin`).
* **Swagger** (`/swagger`, Development): authorise with both the Bearer (static token) and `X-Session-Token`.

### Adding an API

1. **Action** on a controller in `POGovApi/Controllers/V1` (`[HttpPost]`, a request class from `Domain/Entities/Request`
   or `Application/DTOs`, `return Success(data)` / `Ok(response)`; role checks with `RequireStaff()` / `RequireRole(…)`).
2. **API_MASTER row** — add it to the seed list in `database/sqlserver/04_api_master.sql`
   (`(N'<Action>', N'PO_<NAME>', N'<Action>', N'POST', N'application/json', <timeout s>, N'<description>')`) and run the script
   (it only inserts missing task names), or insert the row by hand on each server.
3. **AllApiNames** — `public static string PO_<NAME> { get; set; } = nameof(PO_<NAME>);` in
   `Domain/Entities/Common/AllApiNames.cs` and `public string? PO_<NAME> { get; set; }` in `ApiNames.cs`.
4. **ApiNamesSettings** — `"PO_<NAME>": "PO_<NAME>"` in `Admin/appsettings.json`.
5. Call it from the portal: `ApiMasterService.CallingAPI<ApiDataResponse<T>, TReq>(AllApiNames.PO_<NAME>, req)` and restart
   the portal (it reads API_MASTER at start-up).

`tests/POGovernance.Tests/Architecture/ApiMasterConventionTests` fails when one of steps 1–4 is missing (it also checks that
`POGovApi/Controllers/Sap` serves exactly the four spec paths with the `SapBasic` scheme).

## Testing

```bash
dotnet test
# + the SQL Server store tests (skipped otherwise; each run creates and drops its own database):
POGOV_SQLSERVER_TEST="Server=localhost,1433;User Id=sa;Password=…;TrustServerCertificate=True" dotnet test
```

Most tests are **parity tests**: a Node script in `tests/parity/<area>/` runs the original functions extracted
from `legacy/python-app/app/index.html` on a fixture dataset and writes the expected results; the xUnit tests check that the C# port
(the `DecisionEngine` layer) returns the same decisions, quantities, reasons, texts and Excel output. To regenerate the expected results after a deliberate rule change, run the
harness again (each harness has the command in its header comment). `tests/POGovernance.Tests/Architecture` checks that
`DecisionEngine` stays dependency-free. `tests/POGovernance.Tests/Sap` covers the SAP interface: the verdict mapping (every engine
outcome, Block → HOLD / BLOCK), input normalisation, idempotency / 409 / timeout → 503, events (parking, duplicates, LATE), the
IP allow-list, and contract tests that compare the classes and real answers with `po-governance-sap-api.yaml` (YamlDotNet, test
project only); `tests/POGovernance.Tests/SqlServer/SapSqlServerTests` runs the PRO_SAP_* procedures on SQL Server.

## Documents

- `docs/user-manual/PO-Governance-User-Manual.md` (+ `.pdf`) — user manual for buyers, category heads, planners and admins.
- `docs/security/HRMS-Login.md` (+ `.pdf`) — employee sign-in with Employee ID + HRMS password (only registered employees; QPS gaps found and how they are closed; HRMS set-up).
- `docs/data-automation/Data-Setup-Automation-Design.md` (+ `.pdf`) — design for replacing the manual Data Setup uploads with direct pulls from the source systems (SQL Server linked server or API) and the live SAP position.
- `docs/sap/SAP-API-Reference.md` (+ `.pdf`) — the SAP endpoints with requests and responses, for SAP programmers.
- `docs/database/er-diagram.png` — the database entity diagram.
- `docs/CONVERSION-GUIDE.md` — conventions used for the port.
- `docs/ui/*.md` — screen specifications (what each screen shows and where it comes from).
- `docs/sap/README.md` — the SAP R/3 interface for the SAP / ABAP team and admins (connection, technical users, IP allow-list, examples, verdict mapping, reason codes, shadow / live, timing, pending items).
- `database/README.md` — database setup and moving from v1.1.4.
- `legacy/python-app/DEVELOPER-HANDOVER.md` — the v1.1.4 handover (business rules, SAP / BW plans).

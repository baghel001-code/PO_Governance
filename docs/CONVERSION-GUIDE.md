# PO Governance — .NET conversion guide

The Python + browser tool (v1.1.4, kept unchanged in `legacy/python-app/`) is being rebuilt as an
ASP.NET Core 8 solution with clean architecture, following the layout of the QPS solution.

## Solution layout

| Project | Kind | Holds |
|---|---|---|
| `Domain` | class library | Constants, Entities, Enums, Exceptions, Records, the JS-compatibility helpers (`Domain.Entities.Common.Js`, `SheetRow`) and the engine data model (`Domain.Entities.Engine`) |
| `Application` | class library | Interfaces (`Interfaces/V1`), DTOs, AutoMapper profiles, and **all business logic**: decision engines, recommendation engine, Tool 2 assessment, analytics, accounts / requests / approvals services |
| `Infrastructure` | class library | PostgreSQL store (Dapper + Npgsql), Excel / ZIP readers, AI client, backups, middleware, `ServicesConfiguration` (DI registration) |
| `POGovApi` | ASP.NET Core Web API | `Controllers/V1/*` — every endpoint the portal uses (and later the SAP `/api/sap/v1/*` endpoints) |
| `Admin` | Blazor Server (MudBlazor) | The portal. Talks to `POGovApi` over HTTP only (`APICallService`) — never to the database |
| `tests/POGovernance.Tests` | xUnit | Unit + parity tests |

Dependency direction: `Domain` ← `Application` ← `Infrastructure` ← `POGovApi`; `Admin` → `Application` (DTOs only) + `Domain`.

## Rules for porting the decision logic

1. **The decision rules move from the browser to the server** (handover §8 item 1). Port each JS function
   from `legacy/python-app/app/index.html` faithfully — same branching, same thresholds, same rounding,
   same reason texts. Keep the JS function name in an XML doc comment (`/// Port of index.html runEngineB (line 3847)`)
   so reviewers can compare side by side.
2. Use the helpers in `Domain.Entities.Common.Js` for anything whose JS semantics differ from C#:
   `Js.ParseFloat`, `Js.Num` (the tool's `num()`), `Js.Sf` / `Js.N` (recommendation engine `_sf` / `_n`),
   `Js.Round` (JS `Math.round` — **never** use `Math.Round`, which is banker's rounding),
   `Js.ToFixed`, `Js.Fmt` (`Math.round(v).toLocaleString('en-IN')`), `Js.LocaleIN`, `Js.FmtCr`, `Js.NumberToString`.
3. Rows of uploaded files are `SheetRow`; `row.Col("Generic Material", "GENERIC MATERIAL")` is the JS `col(row, ...)`.
4. Engines read an immutable `EngineData` snapshot (`S.files` + `S.params`), obtained from `IDatasetCache`.
   Per-data-version lookup maps (the JS `_idx` caches) go in `EngineData.GetOrBuild(name, builder)`.
5. JS overrides: when `index.html` defines a function twice (e.g. `plannerTab`, `categoryTab`), the **later**
   definition is the one that runs. `shared.js` also wraps some page functions — read it before porting a flow.
6. Engines return typed result objects (DTOs) instead of HTML. The HTML a JS function built is reproduced in
   the Blazor portal; texts (reasons, notes, labels) are produced on the server so both sides read the same.
7. Parity: for each engine, `tests/parity/` holds a Node harness that runs the original JS functions on a
   fixture dataset and writes the expected results; the xUnit tests assert the C# port returns the same.

## Storage

PostgreSQL schema v2 of v1.1.4 is kept as is (`app_settings`, `app_users`, `app_sessions`, `doc_stamps`,
`dataset_versions`, `upload_log`, `upload_batches`, `request_events`, `run_index`, `run_files`, `audit_log`,
`feedback_log`, `schema_info`), so the .37 database can be used directly by the new version. Documents and
log records keep their JSON shape.

## API conventions

* Routes: `api/v1/<area>/…`. Responses are `ApiResponse<T>` (`ResponseCode` 0 = OK, else the HTTP status,
  `ResponseMessage` = the user-facing reason, same wording as v1.1.4's `{"error": …}`).
* Auth: `POST api/v1/auth/login` returns a session token (stored in `app_sessions`, same as v1.1.4); the portal
  sends it as `Authorization: Bearer <token>`. Roles: admin, planner, cathead, buyer — enforced on the API.
* Business errors throw `Domain.Exceptions.AppException` subclasses; middleware turns them into `ApiResponse`.

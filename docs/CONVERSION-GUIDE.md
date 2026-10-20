# PO Governance — .NET conversion guide

The Python + browser tool (v1.1.4, kept unchanged in `legacy/python-app/`) is being rebuilt as an
ASP.NET Core 8 solution with clean architecture, following the layout of the QPS solution.

## Solution layout

| Project | Kind | Holds |
|---|---|---|
| `Domain` | class library | Constants (roles, app constants), store entities, exceptions, records, settings |
| `DecisionEngine` | class library, **BCL only** | **All verdict algorithms** (the core business-logic layer): `Model` (`EngineData`, `EngineParams`, `DatasetPayload`, `DatasetKeys`, `Decisions`, `SheetRow`), `Common` (`Js`, `JsObjectMap`, `JsOps`, `JsSort`, `MonthMath`, `PrevMonthCarry`, `LedgerHelpers`, `ResolveIndex`, `BsrPeerRank`), `Tool1`, `Tool2`, `Recommendation`, `Grc`, `Analytics/{AttrAnalysis,Insights,PlannerDashboard,Ai,Help}`, `Results/<Area>` (result models). Public façades: `ITool1DecisionEngine`, `ITool2AssessmentEngine`, `IRecommendationEngine`, `IGrcBacktestEngine`, `ISeasonSelectionEngine`, `IAttributeAnalysisEngine`, `IInsightsEngine`, `IAiAssistantEngine`, `IHelpContentEngine` — see `DecisionEngine/README.md` |
| `Application` | class library | IO interfaces (`Interfaces/V1`: `IDatasetCache`, `IRequestLedger`, `IAppClock`, `IStore` …), Ingest / Platform DTOs, and the **use-case services** that load data and call the engines (Tool 1 / Tool 2 / recommendation / analytics), ingest, accounts / requests / approvals services, DI registration |
| `Infrastructure` | class library | PostgreSQL store (Dapper + Npgsql), Excel / ZIP readers, AI client, backups, middleware, `ServicesConfiguration` (DI registration) |
| `POGovApi` | ASP.NET Core Web API | `Controllers/V1/*` — every endpoint the portal uses (and later the SAP `/api/sap/v1/*` endpoints) |
| `Admin` | Blazor Server (MudBlazor) | The portal. Talks to `POGovApi` over HTTP only (`IAPICall.CallingAPI(AllApiNames.X, req)`, Infrastructure `APICallService`, QPS) — never to the database |
| `tests/POGovernance.Tests` | xUnit | Unit + parity tests |

Dependency direction: `Domain` and `DecisionEngine` are independent of each other and of everything else; `Domain` + `DecisionEngine` ← `Application` ← `Infrastructure` ← `POGovApi`; `Admin` → `Infrastructure` (only `APICallService`, like QPS) + `DecisionEngine` (result models) + `Application` (DTOs, `IAPICall`) + `Domain`.
`DecisionEngine` must never gain a `ProjectReference` / `PackageReference` (enforced by `tests/POGovernance.Tests/Architecture`).

## Rules for porting the decision logic

1. **The decision rules move from the browser to the server** (handover §8 item 1). Port each JS function
   from `legacy/python-app/app/index.html` faithfully — same branching, same thresholds, same rounding,
   same reason texts. Keep the JS function name in an XML doc comment (`/// Port of index.html runEngineB (line 3847)`)
   so reviewers can compare side by side.
2. Put the port in `DecisionEngine` (pure, synchronous; "today" / "now" are parameters, never a clock read; rule errors are
   `EngineValidationException` / `EngineNotFoundException`, which the Application service translates). The Application
   service only loads the data and calls the engine.
3. Use the helpers in `DecisionEngine.Common.Js` for anything whose JS semantics differ from C#:
   `Js.ParseFloat`, `Js.Num` (the tool's `num()`), `Js.Sf` / `Js.N` (recommendation engine `_sf` / `_n`),
   `Js.Round` (JS `Math.round` — **never** use `Math.Round`, which is banker's rounding),
   `Js.ToFixed`, `Js.Fmt` (`Math.round(v).toLocaleString('en-IN')`), `Js.LocaleIN`, `Js.FmtCr`, `Js.NumberToString`.
4. Rows of uploaded files are `SheetRow`; `row.Col("Generic Material", "GENERIC MATERIAL")` is the JS `col(row, ...)`.
5. Engines read an immutable `EngineData` snapshot (`S.files` + `S.params`), obtained from `IDatasetCache`.
   Per-data-version lookup maps (the JS `_idx` caches) go in `EngineData.GetOrBuild(name, builder)`.
6. JS overrides: when `index.html` defines a function twice (e.g. `plannerTab`, `categoryTab`), the **later**
   definition is the one that runs. `shared.js` also wraps some page functions — read it before porting a flow.
7. Engines return typed result objects (DTOs) instead of HTML. The HTML a JS function built is reproduced in
   the Blazor portal; texts (reasons, notes, labels) are produced on the server so both sides read the same.
8. Parity: for each engine, `tests/parity/` holds a Node harness that runs the original JS functions on a
   fixture dataset and writes the expected results; the xUnit tests assert the C# port returns the same.

## Storage

PostgreSQL schema v2 of v1.1.4 is kept as is (`app_settings`, `app_users`, `app_sessions`, `doc_stamps`,
`dataset_versions`, `upload_log`, `upload_batches`, `request_events`, `run_index`, `run_files`, `audit_log`,
`feedback_log`, `schema_info`), so the .37 database can be used directly by the new version. Documents and
log records keep their JSON shape.

## API conventions

QPS style — the full description, and how to add an API, is in the README section *API conventions*.

* Routes: `[Route("api/v1/[Action]")]`, POST actions with typed request classes (`Domain/Entities/Request`), `return Ok(response)`
  with a `BaseResponse` (`ApiDataResponse<T>`: `responseCode` 0 = OK, else the HTTP status; `responseMessage` = the user-facing
  reason, same wording as v1.1.4's `{"error": …}`; `Data`). `PropertyNamingPolicy = null`.
* The portal calls by task name: `CallingAPI(AllApiNames.PO_…, req)` → the `API_MASTER` row (URL, method, timeout, static token).
* Auth: `Authorization: Bearer <static API token>` (StaticTokenMiddleware, `JwtSettings:SecretKey`) + `X-Session-Token: <session>`
  from `AuthLogin` (stored in `app_sessions`, same as v1.1.4). Roles: admin, planner, cathead, buyer — enforced on the API.
* Business errors throw `Domain.Exceptions.AppException` subclasses; middleware turns them into `ApiDataResponse`.

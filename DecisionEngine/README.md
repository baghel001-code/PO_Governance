# DecisionEngine — the core business-logic layer

`DecisionEngine` holds **every verdict algorithm** of VMM PO Governance: the Tool 1 decisions
(New Request), the Tool 2 PO assessment, the recommendation engine, GRC back-testing / season selection
and the analytics calculations (attribute analysis, insights dashboards, AI prompt context, help values).
It is a plain class library that any host can reuse (the API, a batch job, the future SAP endpoints, tests).

## The rule: no dependencies

- `DecisionEngine.csproj` has **no `ProjectReference` and no `PackageReference`** — BCL only
  (`System.Text.Json`, `System.Text.Json.Nodes`, `System.IO.Compression`, `System.Text.RegularExpressions`…).
- It knows nothing about databases, HTTP / ASP.NET, DI containers, logging, configuration, users or sessions,
  and nothing about the `Domain`, `Application` or `Infrastructure` projects.
- APIs are **synchronous, deterministic and side-effect free**. Inputs: a data snapshot (`EngineData`), parameters,
  for the same-day ledger today's requests as `JsonObject`s, and "today" (`"yyyy-MM-dd"`) / "now" (epoch ms) —
  the engines never read a clock. Outputs: the result models in `Results/`.
- Rule errors are `DecisionEngine.Common.EngineValidationException` (bad / missing input, v1.1.4 toast text) and
  `EngineNotFoundException`. The Application services translate them into the platform's `AppException`s
  (`Application.Services.V1.EngineErrors`).
- `tests/POGovernance.Tests/Architecture/DecisionEngineArchitectureTests.cs` fails the build's tests if the csproj gains a
  reference, if the assembly references anything outside `System.*` / `netstandard` / `mscorlib`, or if `Domain`
  starts referencing this layer.

Dependency direction: `Domain` and `DecisionEngine` are independent of each other; `Application` references both
(use cases load data through its IO interfaces and call the engines); `Infrastructure`, `POGovApi`, `Admin` and the
tests reference what they need.

## Public engines (façades)

| Interface | Default implementation | What it does |
|---|---|---|
| `Tool1.ITool1DecisionEngine` | `Tool1DecisionEngine` | Form options, input checks, `Decide` (routing → Engine B / Engine A / NEW, Confirm Final Quantity, STR caution), Engine B re-check, result card |
| `Tool2.ITool2AssessmentEngine` | `Tool2AssessmentEngine` | Assess every F2 line, assessable-line count, the `Tool2Workspace` (views, filters, roll-ups, CSV / Excel model), run summary |
| `Recommendation.IRecommendationEngine` | `RecommendationEngine` | `_runB` / `_runA` / `_strCaution` / `_coreColourSTRFast` …, the whole modal (`View`, `Months`, `ExportView`) |
| `Grc.IGrcBacktestEngine` | `GrcBacktester` | GRC Impact card, Article Trace, Row-detail export |
| `Grc.ISeasonSelectionEngine` | `SeasonSelectionEngine` | Sorted Article Master seasons + effective Current / Fallback selection, pill toggle |
| `Analytics.AttrAnalysis.IAttributeAnalysisEngine` | `AttributeAnalysisEngine` | Brand → MC → MRP × Flag drill-down levels 1–3 |
| `Analytics.Insights.IInsightsEngine` | `InsightsEngine` | Category Insights dashboard (Tool 1 requests), Planner dashboard (Tool 2 run rows) |
| `Analytics.Ai.IAiAssistantEngine` | `AiAssistantEngine` | Chat context / system prompt, AI Insights summary + prompt, response parsing (no transport) |
| `Analytics.Help.IHelpContentEngine` | `HelpContentEngine` | Parameter-dependent "How it works" values |

All implementations are stateless and thread-safe; construct them directly (`new Tool1DecisionEngine()`) or let the
host register them (the API does it in `Application/DIResolver/DecisionEngineRegistration.cs`).

### Usage

```csharp
using System.Text.Json.Nodes;
using DecisionEngine.Model;
using DecisionEngine.Recommendation;
using DecisionEngine.Tool1;
using DecisionEngine.Tool2;

// 1. Build the snapshot: rows per data file (DatasetKeys.F1 … Grc) + the planner parameters.
var files = new Dictionary<string, IReadOnlyList<SheetRow>?>
{
    [DatasetKeys.F1] = f1Rows, [DatasetKeys.F2] = f2Rows, [DatasetKeys.F3] = f3Rows,
    [DatasetKeys.F4] = f4Rows, [DatasetKeys.Um] = umRows, [DatasetKeys.Bsr] = bsrRows,
};
var data = new EngineData(files, new EngineParams { T1 = 75, T2 = 100 }, dataVersion: 1, paramsVersion: 1);
IReadOnlyList<JsonObject> todaysRequests = ledgerOfToday;      // stored v1.1.4 request objects
const string today = "2026-10-20";

// 2. Tool 1 — decide a new request.
ITool1DecisionEngine tool1 = new Tool1DecisionEngine();
var outcome = tool1.Decide(data, todaysRequests, today, id: 1, gm: "1101000101", colour: "NAVY",
                           month: "11.2026", qty: 500, confirmation: null);
Console.WriteLine($"{outcome.Decision}: {outcome.DecisionReason}");

// 3. Tool 2 — assess every released PO line.
ITool2AssessmentEngine tool2 = new Tool2AssessmentEngine(new RecommendationEngine());
var ctx = new RecommendationContext(data, todaysRequests, today);
var lines = tool2.Assess(ctx);
var view = tool2.Workspace(lines, ctx).BuildView();
```

## Folders and the JavaScript they port

Every class keeps an XML doc comment naming the original function in `legacy/python-app/app/index.html`
(or `shared.js`) and its line, e.g. `/// Port of index.html runEngineB (line 3847)`.

| Folder / namespace | Ports |
|---|---|
| `Model/` | `S.files` + `S.params` (`EngineData`, `EngineParams`), the stored data-file payload (`DatasetPayload`), dataset keys, decision labels, uploaded-file rows (`SheetRow`, the JS `col(row, …)`) |
| `Common/` | JS semantics (`Js`: `parseFloat`, `num`, `Math.round`, `toFixed`, `toLocaleString('en-IN')` …), JS object key order (`JsObjectMap`), stable sort (`JsSort`), month math, `_prevMonthCarry`, ledger field access, `_idx.resolveMap` / `resolveGM` / `routeRequest`, `getWOF` / `getPDSTR` / `quartile` / `pctRank` (`BsrPeerRank`), engine exceptions |
| `Tool1/` | `formSubmit` flow, `routeRequest`, `runEngineB` (line 3847), `runEngineA` + `absBlock` (lines 4257–4270), `qcConfirm` / `qcDecline`, `renderResult` card, `formPopulate*`, `renderCatHistory`, shared.js `colourKey` / `slimRequest` / `approvalBadge` |
| `Tool2/` | `runTool2Batch`, `runEngineB2Fast`, Engine A mapping (`Tool2EngineA` → the shared `Tool1EngineA`), Tool 2 views / filters / roll-ups / exports (`Tool2Workspace`), shared.js `runSummary` |
| `Recommendation/` | Second `<script>` (lines 9369–11512): `_runB`, `_runA`, `_strCaution`, `_coreColourSTRFast`, `_bsrQ`, `_recQty`, `_paintRec`, `_exportEngineARecs`, `_openRec` / `_render` |
| `Grc/` | GRC back-testing (lines 2685–3643): `renderGRCImpact`, `renderArticleTrace`, `exportGRCRowDetail`, period math; Season Selection (`_seasonSortKey`, `_allSeasonsSorted`, `renderSeasonPills`, `_toggleSeasonPill`) |
| `Analytics/AttrAnalysis/` | `_aaBuildTree` … `_aaRenderLevel3` (lines 9516–9902) |
| `Analytics/Insights/` | `renderCatInsights` (line 8622) |
| `Analytics/PlannerDashboard/` | `renderPlannerDashboard` (line 9062) |
| `Analytics/Ai/` | `sendAIChat` + `_aiBuildContext*` (lines 8859–9046), `_aiBuildSummary` … `renderAIInsights` (lines 11516–11732) |
| `Analytics/Help/` | `renderEngineBHelp` / `computeFor` (lines 1948–1985) |
| `Analytics/Common/` | Shared analytics helpers (`AnalyticsJs`, dashboard math) |
| `Results/<Area>/` | The result models the engines return (and the request / query models they take); JSON names match the v1.1.4 objects |

**Engine A** (`runEngineA`) exists once: `Tool1/Tool1EngineA.cs`. Tool 1 and Tool 2 call the same JS function, so Tool 2
uses it through the thin adapter `Tool2/Tool2EngineA.cs`, which only maps inputs (`GmInfo` → `Tool1InfoDto`) and the result to
Tool 2's line model. Both tools' parity suites pass on the single implementation.

## How parity is proven

`tests/parity/<area>/` holds Node harnesses that load the original functions from `legacy/python-app/app/index.html`,
run them on fixture datasets and write the expected results (`expected.json`). The xUnit tests in
`tests/POGovernance.Tests/<Area>/` run these engines on the same fixtures and assert the same decisions, quantities,
reasons, texts and Excel output (1330+ assertions-heavy tests). After a deliberate rule change, re-run the harness
(the command is in each harness's header comment) and commit the new expected results.

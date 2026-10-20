# Analytics screens — UI spec (Attribute Analysis, Category Insights, Planner dashboard, AI, How it works)

Ports of the remaining v1.1.4 analytics views of `legacy/python-app/app/index.html`. All figures, texts
and colours are produced on the server; the portal lays out the DTO fields named below (all in
`DecisionEngine.Results.Analytics.*`). Colours are hex values unless noted; CSS variables refer to the portal
theme (`--vmm-pink`, `--vmm-cyan`, `--vmm-navy`, `--ink2/3`, `--sur2/3`, `--brd`, `--grn`, `--grnl`). Number
texts are already formatted (Indian grouping). Responses are wrapped in `ApiResponse<T>`.

| Screen | Endpoint | Roles | DTO |
|---|---|---|---|
| 🔎 Attribute Analysis L1 | `GET api/v1/attribute-analysis?brand=&mc=&minStock=` | staff | `AaLevel1Dto` |
| … L2 (MRP × Flag) | `GET api/v1/attribute-analysis/mc?brand=&mcKey=` | staff | `AaLevel2Dto` |
| … L3 (bucket) | `GET api/v1/attribute-analysis/bucket?brand=&mcKey=&mrp=&flag=` | staff | `AaLevel3Dto` |
| 📊 Category Insights | `GET api/v1/insights/category?brand=&division=&mc=&month=&engine=&decision=&from=&to=` | any | `CatInsightsDto` |
| Planner dashboard | `GET api/v1/insights/planner?runId=&brand=&division=&mc=&mrp=&flag=&deliveryMonth=&engine=&decision=` | staff | `PlannerDashboardDto` |
| Assistant header | `GET api/v1/assistant/chat/info?scope=tool1\|tool2` | any / staff for tool2 | `AssistantChatInfoDto` |
| Assistant question | `POST api/v1/assistant/chat` body `AssistantChatRequest` | any / staff for tool2 | `AssistantChatResponse` |
| 🤖 AI Insights | `POST api/v1/assistant/insights` body `{focus, runId}` | staff | `AiInsightsDto` |
| How it works values | `GET api/v1/help/params` | any | `HelpParamsDto` |

The raw provider proxy (`api/v1/ai/chat`, `api/v1/ai/insights`) belongs to the platform and is not used by these screens.

---

## 1. 🔎 Attribute Analysis (planner tab, markup `view-attranalysis` ~line 1161)

Header: title "🔎 Attribute Analysis", pink→cyan 60×3 underline, subtitle *"Brand → MC → MRP × Flag
drill-down. Sales Mix / Stock Mix / Deviation for every attribute in Article Master, plus colour-level STR at
bucket level. Independent of Attribute Relevance Master."* Sidebar button "↻ Refresh" = reload level 1.

**Filter bar** (grey `#F9FAFB` card, re-query level 1 on every change, *as you type* for the inputs):
Brand select ("All Brands" + `AaLevel1Dto.Brands`), "MC (code or desc)" text input (placeholder "Search MC…"),
"Min Stock" number input (placeholder 0), "Clear" button (empties all three).

**Breadcrumb** (11px ink3, parts joined by " ▸ "): `Breadcrumb[]`; a part with `Level` 1/2 is a link (navy,
underlined) back to that level; `Level == null` is the current position.

**Legend** (`Legend[]`, 10px ink3): 9px dot of `Color` + `Label`.

### Level 1 — Brand → MC (`AaLevel1Dto`)
- `State == "noData"`: card, 📂 (36px), `EmptyTitle` (13px ink2), `EmptyMessage` (12px). No filters result.
- `State == "noMatch"`: breadcrumb + card "No Brand/MC combinations match the current filters." (`EmptyMessage`).
- Table columns: Brand (bold) · MC CD (mono) · MC Desc · Stock (right, mono, bold, colour `Status.Color`) ·
  Sales (45d) · MRP×Flag Buckets. Rows sorted by stock desc. Each row: 3px left border `Status.Color`,
  tooltip `Tooltip`, pointer; click → level 2 with (`Brand`, `McKey`).

### Level 2 — MRP × Flag (`AaLevel2Dto`)
Breadcrumb "Brand → MC" (link) ▸ "{brand} · {mcDesc} ({mcCd})". Legend. Table: MRP (`MrpText`, "₹399") · Flag ·
Stock (coloured as L1) · Sales (45d) · # Colours. Row border/tooltip as L1; click → level 3 with (`Brand`,
`McKey`, `Mrp`, `Flag`).

### Level 3 — bucket (`AaLevel3Dto`)
1. Breadcrumb: Brand → MC (L1) ▸ "{brand} · {mcDesc}" (L2) ▸ "₹{mrp} · {flag}".
2. KV chips (`Summary`: Brand Type, MC, MC Desc, MRP, Flag, Total Stock, Total Sales (45d)) — white chips,
   8.5px grey uppercase label, 11.5px bold mono value.
3. `Note` when set: `NoteKind == "warning"` → amber card (`#FFFBEB` bg, `#FDE68A` border, `#92400E` text);
   `"info"` → plain card, ink3 text.
4. `DeviationLegend` (when not empty): blue = chase risk, green = balanced ±5pp, red = overstock risk.
5. One table per `AttributeTables[]`: heading `Title` (12px bold navy); columns {Dimension} · Stock · Stock Mix% ·
   Sales (45d) · Sales Mix% · Deviation (Sales − Stock). Deviation cell bold, colour `DeviationColor`. Rows with
   `Unmapped` are grey italic ("(No Fit tag in Article Master)"). Footer row `Total` (bold, 2px top border).
6. `ColourTitle` heading, then 5 band cards (`Bands[]`, flex, min 120px): 4px left border `Color`, 9px uppercase
   `Label`, 16px bold `Count` in `Color`, 10px `StockText` ("12,345 units").
7. Colour table (`ColourRows[]`): Colour as a pill (`#E0F6FF` bg, `#006B99` text) · Stock · Stock Mix% · Sales (45d) ·
   Sales Mix% · STR (ROS) (`StrText`, bold, `Status.Color`). Row left border `Status.Color`.

STR bands / `Status`: < 30 Fast blue `#2563EB` · < 75 Healthy green `#059669` · < 120 Slow amber `#D97706` ·
≥ 120 Very Slow red `#DC2626` · no sales grey `#6B7280` (light backgrounds in `Status.Background`).

---

## 2. 📊 Category Insights ("Category Dashboard", markup `view-cat-insights` ~line 1268)

Data: the Tool 1 requests the user may see (v1.1.4: today's requests; `from`/`to` widen the range).
Header: chart icon + "Category Dashboard", underline, subtitle *"This portal exists to stop weak sell-through
from turning into a PO — every number below traces back to that one goal: catch it before the buy, not after
the markdown."*, button "↻ Refresh".

**Filters** (grey card, re-query on change): Brand (`Options.Brands`, "All Brands"), Division (`Divisions`,
"All Divisions"), MC Desc (`Mcs`, "All MCs"), Month (`Months`, "All Months"), Engine (`Options.Engines`),
Decision (`Options.Decisions`), "Clear Filters" (= query without filters). Show the values from `Applied`
(a filter value that disappeared from the options is dropped, like the browser did).

**Empty states**: `State == "noRequests"` → empty block (chart icon, `EmptyTitle` "No insights yet",
`EmptyMessage`, pink button `EmptyAction` "New Request →" → New Request tab). `State == "noMatch"` →
"No requests match these filters", `EmptyMessage`, pink button "Clear Filters".

**Body** (`State == "ok"`), top to bottom:
1. KPI grid (`Kpis`, auto-fit min 120px): tile bg `Background`, border `{Color}33`, 19px bold mono `Value`
   in `Color`, 9px uppercase `Label`. Tiles: Total Requests ("12 / 40" when filtered), Auto Allow, Watch List,
   Blocked / Hold, Qty Requested, Qty Approved, Headroom Utilised.
2. Card `DecisionMix.Title`: SVG donut 120×120 viewBox 0 0 42 42 rotated −90°, one `<circle cx=21 cy=21
   r=15.9155 fill=transparent stroke-width=6>` per `Segments[]` with `stroke=Color`,
   `stroke-dasharray="{DashArray} {DashGap}"`, `stroke-dashoffset=DashOffset`. Legend: 10px square `Color`,
   Decision, bold mono Count, "({PctText}%)".
3. Card `Drivers.Title` ("🔎 What's Driving Watch List / Block Decisions"): per `Rows[]` label + "{Count} · {Pct}%"
   and an 8px bar (`--sur3` track, `--vmm-pink` fill `Pct`%). `EmptyMessage` instead when there are no rows.
4. Table `McRisk.Title`: MC Desc · Requests · Qty (`QtyText`) · Auto Allow (green `#00873E`) · Watch List
   (`#B45309`) · Block/Hold (`#DC2626`) · Risk % (bold, `RiskColor`). Max 20 rows.
5. Table `Flagged.Title` (only when `Flagged != null`): # (`Id`) · Generic Material (mono) · Colour · MC Desc ·
   Decision (pill class `DecisionPill`) · Driver (10px ink3). Most recent first, max 25.
6. Card `Recommendations.Title`: each `Items[].Html` (contains `<strong>`; render as markup or use `Text`) in a
   `--sur2` box with a 3px pink left border.

---

## 3. Planner dashboard (Tool 2 run)

v1.1.4 kept `renderPlannerDashboard` but **no markup exists** in index.html (nor in orig.html) — it returned
early. Suggested placement: a section of the Tool 2 (PO Assessment) results page. Ids it expected:
`planner-dash-placeholder`, `planner-dash-filters`, `planner-dash-body`, filters `pd-f-*`.

- `State == "noRun"`: show a placeholder ("Run an assessment first"), hide the filters.
- Filters: Brand, Division, MC (`Mcs`), MRP (`Mrps`, numeric order), Flag, Delivery Month, Engine
  (`Options.Engines`), Decision (`Options.Decisions`); `Applied` = effective values; reset = empty query
  (`_plannerDashResetFilters`). `runId` selects a saved run (default latest).
- `State == "noMatch"`: "No lines match these filters" + `EmptyMessage` + "Clear Filters".
- Body: KPI tiles (18px values; Total Lines, Retain, Watch List, Reduce/Cancel, Qty at Risk, Value at Risk (₹)),
  Decision Mix donut (colours Retain `#00873E`, Watch `#B45309`, Reduce `#D97706`, Cancel `#DC2626`,
  Cannot Validate `#6B7280`), Drivers card ("🔎 What's Driving Reduce / Cancel / Watch List"), MC table (MC Desc ·
  Lines · Retain · Watch List · Reduce/Cancel · Value at Risk (₹) (`ValueAtRiskText`) · Risk %), Recommendations
  ("💡 Recommendations for the Planning Team"). Same styling as the Category dashboard.

`clearAllRequests` (Clear All on Today's Requests) only calls the platform's `DELETE api/v1/requests?date=today`
after the confirm text of v1.1.4 (*"Clear ALL of today's requests for EVERY user? …"*), then reloads the
request views — nothing in this area.

---

## 4. AI

### VMM Assistant chat (floating panel — hidden by CSS in v1.1.4, `#ai-chat-fab{display:none!important}`)
- Floating 56px round button (pink→cyan gradient, chat icon) bottom-right; panel 380×560, navy header
  "VMM Assistant" + badge `ScopeBadge` ("Category · Tool 1" / "Planner · Tool 2"; scope = planner view → tool2).
- `Enabled == false`: show `DisabledTitle` / `DisabledMessage` instead of the conversation.
- First open: assistant bubble `Welcome`.
- Send (Enter, Shift+Enter = newline): user bubble (navy, right), "Thinking…" bubble, `POST chat` with
  `{scope, question, history, runId}`. Response: `Ok` → assistant bubble `Answer` (white, left, pre-wrap);
  else red error bubble `Error`. Keep `History` from the response for the next question (the server sends the
  last 8 entries + the question, max_tokens 1000; context = aggregate JSON + up to 40 keyword-matched rows).

### 🤖 AI Insights (planner tab, markup `view-aiinsights` ~line 1182)
Header "🤖 AI Insights", subtitle *"Reads the current batch assessment (Cancel / Reduce Qty / Watch List lines)
and reasons across BSR quartile, STR and pending PO in plain language. Requires a Run Assessment first."*
Sidebar: focus select (`risk` Highest-risk lines, `brand` Patterns by Brand/MC, `action` What to action first) and
"✨ Generate Insights" (disabled, "⏳ Thinking..." while waiting).
- Before generating: centred text "Run an assessment in **PO Assessment**, then click **Generate Insights** in the sidebar."
- `Toast` → show as toast. `State` "disabled" / "noRun" → nothing else changes.
- While waiting: KPI strip can be shown with `LoadingText` card.
- KPI strip (`Kpis`, auto-fit min 140px): card with 3px top border `Color`, 22px bold mono `Value`, 10px label.
- Heading "🤖 Cross-Cutting Findings — not visible per-line elsewhere"; cards (`Findings`, auto-fit min 260px,
  4px cyan left border): "{Icon} {Title}" (12px bold), optional `ImpactQtyText` (18px navy mono) + " units impacted",
  `Insight` (11.5px). Escape all texts.
- `Action` callout: 4px `BorderColor` left border, bg `Background`, "✅ RECOMMENDED ACTION" label, `Title`, `Detail`.
- `Meta` right-aligned 10.5px.
- `State` "fallback"/"error": card with `FallbackText` (pre-wrap) instead of the cards.

---

## 5. How it works (category tab, markup `view-cat-howitworks` ~line 1326)

Static text: `Admin/wwwroot/help/hw-banner.html` (dark banner), one fragment per tab
`help/hw-{vision,t1-engA,t1-engB,t2-engA,t2-engB,t2-export,access}.html` (order and titles: `HelpParamsDto.Tabs`;
`vision` is the default) and the styles `help/how-it-works.css`. Tab buttons: `.hw-tab` / `.hw-tab.active`
(navy). Live values: `GET api/v1/help/params` → replace each `{{NAME}}` with `Placeholders[NAME]` (D, BUF, MIX, N,
MWD, T1, T2, EFFT1, EFFT2, T1_CALC, T2_CALC, T1_RESULT, T2_RESULT). Re-fetch after the parameters change.
Fragments are regenerated with `node tests/parity/analytics/extract-help.js`.

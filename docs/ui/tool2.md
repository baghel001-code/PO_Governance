# Tool 2 — "📋 Raised PO Assessment" screen (planner view)

Port of v1.1.4 `view-assess` (index.html ~857–1022) and its JS (`runAssessment` … `exportAssXLSX`, lines
5061–8282) plus shared.js saved runs. **All numbers and texts come from the API** — the portal only lays them
out. Staff only (admin, planner).

## API

| Call | Returns | Used for |
|---|---|---|
| `POST api/v1/assessment/run` | `Tool2RunResultDto { runId, runNo, view: Tool2ViewDto }` | sidebar **▶ Run Assessment** |
| `GET api/v1/assessment/runs/{id}` | `Tool2ViewDto` | History → **Open** a saved run (v1.1.4 runs too) |
| `POST api/v1/assessment/runs/{id}/detail` body `Tool2DetailFilterDto` | `Tool2DetailViewDto` | Detail view tab / filters / decision chips |
| `POST api/v1/assessment/runs/{id}/rollup` body `Tool2RollupFilterDto` | `Tool2RollupDto` | Division × MC + Vendor tables' filters |
| `GET api/v1/assessment/options[?runId=]` | `Tool2OptionsDto` | params, assessable F2 lines (disable Run when 0), filter lists, Category Heads |
| `POST api/v1/assessment/export` body `Tool2ExportRequestDto` | file (xlsx / csv / zip); header `X-Export-Message` = toast | **⬇ Excel** buttons (and CSV) |

Errors are `ApiResponse` messages with the v1.1.4 toast texts (e.g. "Please upload the F2 (Released PO) file in Data
Setup first", "No valid rows found in F2 — …", "No rows match the selected Category Head(s) — export cancelled.").

`runId` of a run that could not be saved starts with `unsaved-` (kept in server memory only); show
`view.saveError` as a warning toast. After a run show `view.toast` ("Assessment complete — 87 lines processed")
and `view.statusText` under the Run button (line count = `lines.length`).

## Page layout (top → bottom)

1. **Title** "📋 Raised PO Assessment", sub "Batch assessment of all lines in F2 (Released PO file). Click Run
   Assessment in the sidebar." Before any run: empty state ("No assessment run yet" / "Upload files in **Data
   Setup**, then click **▶ Run Assessment** in the sidebar.").
2. **History banner** when `view.isHistory`: "📜 **Viewing saved run** of {at} by {by} — dashboard, tables, Excel
   export and parameters are as they were then." + "Not available any more: {missingFiles}" (yellow) when not empty.
3. **KPI strip** — `view.kpis` (8 tiles): big `value` in `color`, small uppercase `label`, tile background `bg`,
   border `color` at 20 % alpha. Order: Total Lines, Retain, Reduce Qty, Cancel, Watch List, Qty at Risk,
   ₹ Value at Risk, % Need Action.
4. **Brand Summary card** (default) — header "📊 PO Assessment — Brand Summary", breadcrumb text ("Collapsed by
   Brand Type · Click to expand"; after a toggle "Expanded - Click rows to drill deeper" / "Collapsed - Click arrow
   to expand"), buttons **⊟ Collapse All**, **☰ Detail View**, Category Head export picker, **⬇ Excel**.
   - Grid columns: Hierarchy | Total POs | Retain | Reduce | Cancel | Watch | % Action | Qty at Risk | ►.
   - `view.tree`: Brand (level 0, dark navy row `#1a2a4a`, white text) → Division (1) → MC (2) → MRP (3, label
     "Rs.{mrp}"). Each row: ► icon (▼ when open), `label`, grey `depth` suffix, `total`, `retain||'—'`,
     `reduce||'—'` (bold when > 0), `cancel`, `watch`, `pct`% in `pctColor`, `atRiskText` (pink `#E8006E`).
     Indent 18 px per level; all collapsed initially; nodes already sorted by Qty at Risk.
   - Expanding an MRP shows its PO table (`rows` = line idx, sorted by qty): PO Number, Gen. Material, Colour,
     Del. Month, Qty ↓ (`fmt(relQty)`), STR (`strText`, red when `strBreach`), Decision pill (hover = `tooltip`),
     Rec. Qty (`recQty>0 ? fmt : '—'`, pink), ▼. The whole row has `tooltip` as title. Clicking expands the line's
     **Detailed Computation** (below) under the header "{decision} - Detailed Computation" on `decisionBg`-ish
     colours (Cancel `#DC2626/#FEF2F2`, Reduce `#D97706/#FFFBEB`, Watch `#92400E/#FFF7ED`, else `#00873E/#DCFCE7`).
5. **Detail View** (replaces the Brand Summary; "← Summary" goes back) — "Action Required — Detailed List".
   - Engine tabs: **Single Concept MRP · Colour STR** (`tab:"B"`, default, active `#00AEEF`) and **Multi Concept
     MRP · Peer BSR** (`tab:"A"`, active `#1A4FAF`).
   - Context banner: **`bannerTitle`** — `bannerText`, colours `bannerColor` / `bannerBg`.
   - Filter card "Filter & Action Selection": Brand Type / Division / MC / MRP / Del. Month drop-downs from
     `view.detailFilters` (first option "All …" = empty value; MRP values carry "₹"), Action chips 🚫 Cancel /
     ⬇ Reduce / 👁 Watch / ✅ Retain / All (`dec`; the active chip gets a 3 px navy ring and scale 1.05),
     Category Head picker + ⬇ Excel. Every change → `POST …/detail`.
   - Card "⚠ Action Required" + `countText` + "· Sorted by highest Qty at Risk · Click row for detailed
     computation"; table header = `header` (label, align, min width). `empty` → "✓ No action required for the
     selected filters".
   - **Tab B** (`months`): per month a navy separator row "📅 {month}", `groupsText`, `excessText` (light red),
     `totalQtyText`. Per colour group: colour chip (`#E0F6FF`/`#006B99`), brand, mcDesc, ₹mrp, flag, dm, poCount,
     totalQtyText, mixText (green when `mixOk` else amber), strText (`strColor`), maxPermText (pink),
     `colourDecision` pill (`decisionColor`/`decisionBg`), ▼. Expand panel (left border `decisionColor`, bg
     `decisionBg`): `panelTitle`; when `whyNoStr` a white box "**Why no STR was computed:** {whyNoStr}"; `kvs`
     chips; when `formula` a monospace box, one line per entry (bold/coloured segments as flagged); "PO-wise
     Allocation (FIFO: largest PO cut first)" table (Colour, Generic Material, PO Number, Released Qty,
     Recommended Action pill in `actionColor`, Max Permissible Qty = `recQty` or "0") + TOTAL row (`totalQty`,
     `totalRetain`).
   - **Tab A** (`rows` = line idx): PO Number, Gen. Material, Colour, Brand, Division, MC Desc, ₹MRP, Del. Month,
     Released Qty, Option Type, Quartile chip (Q1 `#00873E`, Q2 `#059669`, Q3 `#D97706`, Q4 `#DC2626`), Percentile
     ("58th"), Decision pill (hover `tooltip`), ▼ → "{decision} — Detailed Computation" + the line's reason.
6. **Charts** (two cards): "By Division" (`divisionChart`, top 8) and "By Delivery Month" (`monthChart`): label +
   `note` on one line, an 8 px stacked bar with `widths` = [Retain `#00873E`, Reduce `#D97706`, Cancel `#DC2626`,
   Watch `#FBBF24`] in %; the division chart has a legend (Retain, Reduce, Cancel, Watch).
7. **Division × MC — Excess Concentration** (`rollup.divMc`): filter row (Division, MC, MRP, Month from
   `view.rollupFilters`, Decision = `rollupDecisions`, **Clear**) → `POST …/rollup`. Columns Brand, Division, MC,
   Lines, Reduce, Cancel, Watch, Qty at Risk (`atRiskText`), ₹ Value (`valueText`, purple), % of Total Risk
   (`pctText`). Rows with `rows` are clickable (▸/▾) and expand a breakdown table from `rollup.lines[idx]`: GM,
   Colour, MRP (`mrpText`), PO Number, Released, Cut Qty (pink bold when `cutPositive`), Decision chip
   (`decisionColor`). Empty: "No rows match the selected filters".
8. **Engine Split** (`engineSplit`: label, thin bar `width`% in `color` on `bg`, big `count`) and **By Vendor**
   (`rollup.vendors`: Vendor, Total, Retain, Reduce, Cancel, Watch, Qty at Risk, ₹ Value; same expand as 7; empty
   rows show "No action-required lines").

## Detailed Computation of a line (`Tool2LineDto.reason`)

1. `kvs` — chips (small grey uppercase label over bold mono value).
2. "**Why this action:**" + `why` segments (bold where `bold`).
3. `caution` (Multi Concept Q1/Q2 Retain only) — amber box "⚠ Caution — STR"; per item `text` + "▾ Show
   calculation" toggle revealing a table of `cs.chain[0..3]` (Month — first row suffixed " (a)", row `cs.d1Idx`
   highlighted `#FEF3C7` —, Open, +PO, −Fcst, =Close, all `fmt`) and `footer`.
4. `strSteps` (Single Concept with STR) — grey box "**STR Computation Steps:**" with 4 lines.
5. `steps` (Multi Concept with a quartile): "Step 1 — Article & Peer Pool" 5 tiles (`value` over `label`, value in
   `color`); "Step 2 — PDSTR vs Peer Average" 4 tiles; `step3Heading` + a rank bar with lines at 25/50/75 % and a
   13 px dot in `decisionColor` at `step3MarkerPos`%, labels Q4 Slow | Q3 Watch | Q2 Healthy | Q1 Top; `step4`
   "Step 4 — Border Zone Check (PDSTR Gap)" mono box (`threshold`, `gap`, "Result : **{result}**" coloured) + verdict
   bar `message` (`color` on `bg`, left border); `absBlockNote` red bar.

## Category Head export panel

Button "👤 All Category Heads ▾" / "👤 {n} selected ▾"; panel lists `view.catHeads` as checkboxes + Select All /
Clear. The selection is shared by both pickers and cleared on every new run. **⬇ Excel** →
`POST export {runId, catHeads, separateFiles}`: no selection = one workbook for everyone (v1.1.4); one name = that
person's scoped workbook; several names = one workbook per person zipped (`separateFiles:false` = one scoped
workbook, as v1.1.4 did). Show the `X-Export-Message` header as a toast. CSV: `format:"csv"`.

## Other per-line fields

`recommendedActionA`, `rankedOptions` (CSV Option 1–4), `rcOptions` / `rcRecommendation` / `poStatus` (Reduce/Cancel
sheet), `bucket` (sheet bucket), `catHead`, `driver`, `chain` (Single Concept month chain), raw engine figures.

## "How it works" texts

The Tool 2 tabs of the static How-it-works page (index.html lines ~1520–1620: "Tool 2 · Multi Concept MRP · PO
Assessment", "Tool 2 · Single Concept MRP · STR Assessment", "Tool 2 · Recommendations & Excel Export") are copied
verbatim; their live numbers (T2, buffer, effective threshold) come from `view.params` / `options.params`
(`T2`, `strBuffer`, `effT2`, `D`, `mix`, `borderTolerance`, `wof`).

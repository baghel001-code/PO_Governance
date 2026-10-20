# 📈 GRC Back Testing screen (planner / admin) — UI spec

Port of the v1.1.4 planner tab "📈 GRC Back Testing" (`legacy/python-app/app/index.html`, markup
`<div id="view-analytics">` ~line 1091, logic lines 2685-3643). Every figure and text is computed on the
server; the portal only lays out the DTO fields named below. Colours are hex values in the DTO unless
noted; CSS variables refer to the portal theme (`--vmm-pink`, `--vmm-navy`, `--vmm-navy-l`, `--ink2/3`, `--brd2`).

## Endpoints (all `RequireStaff()` except GET seasons)

| Call | Returns |
|---|---|
| `PO_GRC_OPTIONS` → `POST api/v1/GrcOptions` body `GrcImpactRequest` | `GrcControlsDto` |
| `PO_GRC_COMPUTE` → `POST api/v1/GrcCompute` body `GrcImpactRequest` | `GrcImpactDto` (whole GRC Impact card) |
| `PO_GRC_TRACE` → `POST api/v1/GrcTrace` body `{Gm, AsOfPeriod}` | `GrcArticleTraceDto` |
| `PO_GRC_EXPORT` → `POST api/v1/GrcExport` body `{AsOfPeriod}` | `.xlsx` file (`GRC_Row_Detail_yyyy-MM-dd.xlsx`), header `X-Message` = "Exported N rows" (URL-encoded); 404 `ApiDataResponse` "No GRC file loaded" |
| `PO_SEASONS_GET` → `POST api/v1/SeasonsGet` | `SeasonSelectionDto` |
| `PO_SEASONS_TOGGLE` → `POST api/v1/SeasonsToggle` body `{group:"current"\|"fallback", season}` | `SeasonSelectionDto` (not saved — see below) |

**Re-compute on every control change** (the browser re-rendered on each `onchange`): send the current
values of all controls; the response's `Controls` tells you what each select ends on (options + value).
**Persisting:** the browser saved `grcAsOfPeriod`, `seasonCurrent`, `seasonFallback` in `S.params`. After a
compute, save `Controls.AsOfPeriod` as the `grcAsOfPeriod` parameter when it differs; after a pill click,
save `SeasonCurrent` / `SeasonFallback` (and when `SeasonSelectionDto.Changed` on load) through the
parameters endpoint.

## Page header
- Title `GrcTexts.PageTitle` ("🧪 GRC Back Testing"), pink→cyan 60×3px gradient underline, subtitle `GrcTexts.PageSubtitle`.

## Card 1 — `GrcTexts.ImpactCardTitle` + badge "Beta"

### No GRC file (`HasGrcData == false`)
Centered placeholder: 📦 (36px), `PlaceholderTitle` ("No GRC file loaded yet", 13px ink2), `PlaceholderText`
(11.5px, max 380px). Controls are hidden.

### Controls (grey `#F9FAFB` box, two rows) — `Controls`
Row 1:
1. **Assess as of** select (pink border, bold): options `AsOfOptions`, value `AsOfPeriod`. Default = saved
   param if still in the file, else the last period of the file.
2. **View by** select — only when `ViewModeVisible` (weekly GRC files): `ViewModeOptions`
   (`native` "Week (native)", `month` "Month (rolled up)"), value `ViewMode`.
3. Text "Compare periods before" · **cutover** select (`CutoverOptions` = native periods, value `Cutover`,
   default last) · text "against that period onward".
4. Right-aligned button "⬇ Export Row Detail" (white, navy text) → export endpoint with the current as-of.
   After download show a toast with `X-Message`; on 404 toast "No GRC file loaded".

Row 2 (top border):
1. **Brand Type**: first option "" labelled `BrandAllLabel` ("All Brands"), then `BrandOptions`; value `Brand`.
2. **Division**: `DivisionAllLabel` ("All Divisions") + `DivisionOptions`; value `Division`.
3. **Category Head**: `CatHeadAllLabel` ("All Category Heads") + `CatHeadOptions` (alphabetical, "Unassigned" last); value `CatHead`.
4. **Drill into period**: `DrillAllLabel` ("All weeks combined" / "All months combined") + `DrillOptions`; value `DrillPeriod`.
5. Right: "Clear Filters" (white) → recompute with Brand/Division/CatHead/Drill = "" (keep as-of, view, cutover).
6. "↻ Recompute" (pink, white text) → recompute with the same values.

Filter options always come from the **unfiltered** data, so picking a Division never shrinks the other lists.

### Body
If `Message` is set show only it (20px padding, ink3, 12px): "No GRC data to show." or
"No GRC data to show for this filter — try Clear Filters.". Otherwise, in this order:

1. **Coverage warning** (`CoverageWarning`, only when set): red box `#FEF2F2`, border `#FECACA`, text `#7F1D1D`, 11.5px.
2. **Legend** (`Legend`): 9×9 rounded squares with label — "Q1 best" #2DD9C4, "Q2" #6FE0D0, "Q3" #F2B84B, "Q4 worst" #FF6B5E, "Absolute Block" #D8483C.
3. **Composition chart** (`Composition`): one 100 %-stacked vertical bar per period, SVG viewBox 1000×280,
   padding L/R 10, top 10, bottom 36 (plot height 234), gap between bars `max(14, 26 − n)`, bar width
   `(980 − gap·(n−1))/n`. Segments bottom-up in `Segments` order with height `Share × 234`, fill `Color`;
   draw `Label` (white, 600 10.5px monospace) centered in the segment when not null (Pct > 5). Period label
   under each bar (600 10px monospace, ink3). A period with `Total == 0` draws only its label.
4. Section title `QuartileTitle` ("Quartile % by week — precise figures behind the chart above"; 10.5px uppercase ink3).
5. **Quartile % table** (`QuartileTable`): header navy-light background, navy 10px uppercase: `PeriodHeader`, then `Headers`
   (Q1 … Abs. Block, "Wrong Buy Value (Q4+Abs.Block)"). Rows: `Period` (600 monospace), each cell `Text` right-aligned
   bold in the cell's `Color`, `WrongValueText` (monospace). Then `Total` row (2px navy top border, `#F5F8FF`, bold; cells still coloured).
6. **Coverage box** (grey `#F9FAFB`): title `CoverageTitle`; per `Coverage` item: period (56px, monospace), 7px track
   (`--brd2`) with fill `Pct`% in `Color`, then `Text` (bold, `Color`). Colours: ≥80 #0E9C8C, ≥40 #B4790E, else #D8483C.
7. **Breakdown box** (grey): heading `Breakdown.Heading` (uppercase ink3). For each of `Breakdown.Tables` (only non-empty
   ones; By Division, By Category Head, By Brand Type): small uppercase `Title`, table with `Headers`
   (`KeyHeader`, Total GRC Qty, Multi Concept GRC Qty, Judgeable Qty, Wrong Buy Qty, Wrong Buy Value, Coverage,
   Wrong Buy % (of Judgeable), Wrong Buy % (of Multi Concept GRC Qty)). Row cells: `Key`, `TrueTotalText`, `TotalText`,
   `JudgeableText`, `WrongText`, `WrongValueText`, `CoverageText`, `RateText`, `RateOfMcText` — the two rate cells bold in
   `RateColor` (≥20 #D8483C, ≥10 #B4790E, else #0E9C8C). Rows are already sorted worst-rate-first. `Total` row styled
   as the quartile total (uncoloured). Division / Category Head tables follow the drill period; Brand Type always shows all periods.
8. **Two cards side by side** (`BeforeCard`, `WithCard`): white, 12px radius. Card: uppercase `Title`; line
   "Total GRC Qty: **`TrueTotalText`** · Multi Concept GRC Qty: **`TotalText`**"; `CoverageText`; then one line per
   `Buckets` item: colour square + `Bucket` left, `Text` bold monospace right. When `IsEmpty`: grey card with `Title`,
   a line break, `EmptyText` ("No weeks before the cutover.").
9. **Ledger table** (`Ledger`): `Headers` (period, Total GRC Qty, Multi Concept GRC Qty, Coverage, Judgeable Qty,
   Wrong Buying Qty, Rate, Value). Cells: `Period`, `TrueTotalText`, `TotalText`, `CoverageText` (bold, `CoverageColor`:
   ≥80 #0E9C8C else #D8483C), `JudgeableText`, `WrongText`, `RateText`, `WrongValueText`. `Total` row uncoloured.
10. **Month mapping note** — only when `ShowMonthMappingNote`: blue box (`#F0F9FF`, border `#0891B233`, text `#0C4A6E`),
    the two `MonthMappingNoteParagraphs` (bold lead-ins "How weeks are mapped to months (View by: Month):" and
    "Worked example (real week from this calendar):").
11. **Footer** `FooterNote` (11px ink3).

## Card 2 — `GrcTexts.TraceCardTitle` (Article Trace)
Intro `GrcTexts.TraceCardText`; input labelled `TraceInputLabel` (placeholder `TraceInputPlaceholder`, monospace);
"Trace" button (pink) and Enter key → `GET trace/{gm}?asOfPeriod=<current as-of>`.
- `IsBlank` → clear the result area.
- `Error` → red box (`#FEF2F2` / `#FECACA` / `#7F1D1D`, 12px) with the text.
- Otherwise a grey summary strip: GM **`Gm`** (monospace), Brand **`Brand`**, Division **`Division`**,
  MC **`McCd` — `McDesc`**, MRP **₹`Mrp`**, Flag **`FlagText`**, and `SingleConceptNote` in amber `#B4790E` bold when set.
  Table (headers Week, Colour, GRC Qty, Bucket, Reasoning): `Period` (bold mono), `Colour` (mono), `QtyText` (right, mono),
  bucket pill (`Bucket`, text colour `BucketColor`, background `BucketColor` + "22" alpha, 12px radius, bold 11px),
  `Extra` (11px ink3 — numbers bold in the original). `Detail` holds the full explanatory sentence (tooltip).

## Season Selection (Data Setup slot) — `SeasonSelectionDto`
Text: "Reads every distinct Season value found in Article Master. Defaults to the latest season as Current and
the next-latest as Fallback — rolls forward on its own as new seasons appear. Click a pill to add or remove it
from a group; combine two seasons in Current during a transition, then drop back to one when it's over."
Two pill rows "Current Season(s)" / "Fallback Season(s)" listing all `Seasons` (most recent first); a pill is
selected (pink fill, white text) when in `SeasonCurrent` / `SeasonFallback`, else white with `--brd2` border.
Click → `POST seasons/toggle`, show the result, save the params, then recompute the GRC screen if loaded.
`Message` ("No Season column found in Article Master yet.") replaces both rows when there are no seasons.

## Bucket meanings / colours (`GrcBuckets`)
Q1 #2DD9C4 · Q2 #6FE0D0 · Q3 #F2B84B · Q4 #FF6B5E · Abs. Block #D8483C · Too New At The Time #B8C4D9 ·
Insufficient Historical Peer Pool #D9C7EE · Future Period (After As-Of Date) #F5C6E0 · Single Concept — out of
scope #9CA3AF (trace) · No Peer Data → #5B6B85 (trace default). Judgeable = everything except No Peer Data,
Too New, Insufficient Pool, Future Period. Wrong buying = Q4 + Abs. Block.

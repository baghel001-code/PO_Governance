# UI spec — "📊 View Recommendation" modal

Port target: index.html v1.1.4 `openRec` / `_openRec` / `_buildMonthBar` / `_render` / `_paintRec` / `_traceRow` /
`_toggleRecGrp` / `_exportEngineARecs` (lines 10853–11512) + the caution box `_cautionBoxHtml` (line 9998).
Everything is computed on the server — the portal only lays out what `RecommendationViewDto` carries.

## 1. API

| Call | Purpose |
|---|---|
| `PO_RECOMMENDATION_GET` → `POST api/v1/RecommendationGet` body `RecommendationRequestDto {gm?, brand?, mcCd?, mrp?, flag?, div?, mcDesc?, engine?, months?, dimOverride?}` | Whole modal → `ApiDataResponse<RecommendationViewDto>` |
| `PO_RECOMMENDATION_MONTHS` → `POST api/v1/RecommendationMonths` body `{gm}` (or `brand, mcCd, mrp, flag, engine`) | Month bar only (`RecommendationMonthsDto`) |
| `PO_RECOMMENDATION_EXPORT` → `POST api/v1/RecommendationExport` (same body) | `.xlsx` file (`VMM_EngineA_FullDetail_yyyy-MM-dd.xlsx`); 400 "No recommendation rows to export" otherwise |

Open from a Tool 1 result card with `{gm}` (the server resolves the bucket and the engine exactly like the card's
`_recInfo` / `_recEngine`), or with the bucket fields + `engine` ("A" / "B" / "NEW") when there is no GM.
Every interaction (month chip toggle, dimension drop-down) re-POSTs with the new `months` / `dimOverride`; the
three column-group toggles and the trace expanders are **client-only** state (no server call — v1.1.4 repainted
from its cache).

Number formatting: `fmt(n)` everywhere below = `Math.round(n)` with en-IN grouping (`12,34,567`), i.e.
`DecisionEngine.Common.Js.Fmt`. Plain numbers (mix %, dr, str) are shown as JS `String(number)`
(`Js.NumberToString`, e.g. `43.4`, `12.5`, `0`). v1.1.4 used `toLocaleString()` with the browser's default locale —
on the business PCs that is en-IN; the server texts (`summary`, `tip`, `formula`, headers) use en-IN.

## 2. Modal frame

* Overlay `#rec-overlay` (fixed, `rgba(10,20,60,0.74)`, blur 6px, scrollable); box max-width 1180px, white,
  radius 16px; click on the backdrop or the round ✕ button closes.
* Header: **title** = `view.title` (15px, bold, `#1A4FAF`) — `📊 Colour Recommendation — Single Concept MRP` when
  `engine=="B"`, else `📊 Attribute & Colour Recommendation — Multi Concept MRP` (also for "NEW").
  **Subtitle** = `view.subtitle` (11px, `#9CA3AF`): `{div} · {mcDesc} · ₹{mrp} · {flag}`.
* If `view.error` is set (missing F1 / F3 / F4, or for engine A Article Master / Attribute Relevance Master),
  v1.1.4 showed it in an `alert()` and did **not** open the modal: show `view.error` (multi-line, bullets "• ")
  in a blocking dialog and stop.

## 3. Month bar (`_buildMonthBar`)

Box `.rec-mbar` (`#F8FAFF`, border `#E2E8F0`, radius 10). Label "SELECT MONTHS TO SHOW (TAP TO TOGGLE):"
(10px, bold, upper-case, `#9CA3AF`, full width). One pill chip per `view.allMonths` entry (text = "MM.YYYY"),
`on` (filled `#1A4FAF`, white text) when it is in `view.months`.
Click: if on → remove it unless it is the last one selected (then nothing happens); if off → add it and sort
chronologically. Then re-POST with the new `months`. Default selection (no `months` sent): the F4 months from the
server's current month on, max 4; if none, the first 4.

## 4. Body — Single Concept (`view.renderEngine == "B"`, data `view.b` : `RecBResultDto`)

Also used for a Multi Concept bucket whose dimension is **Colour-only** (`view.a.dimMode=="colour-only"`,
`view.b == view.a.data`).

* If `paint.emptyMessage` → centred grey `.rec-empty` text ("No colour data found for this MC×MRP in F1/F3.").
* Section heading `paint.heading`: "Colour-wise Stock & PO Recommendation — {n} colours · ranked Stock High to Low · EffT={b.eff}d".
* Table `.rec-tbl` (header cells `#1A4FAF`, white, 10px upper-case; sub-header rows `#0F3B7A`, `#CADCFC` 9px):
  * Row 1: Rank · Colour · Stock · Sale (Last 45 days) · Stock Mix% · Sale Mix% · STR ROS · then one cell
    spanning `months×3` "Recommendation Qty Month wise".
  * Row 2: 7 empty cells, then one cell per month (colspan 3) = the month "MM.YYYY".
  * Row 3: 7 empty cells, then per month: "Pending PO" · "Today's approved" (tooltip "Quantities approved today
    in Tool 1 (all users), landing in this month") · "Max Permissible Qty".
* One row per `b.rows[i]` (already sorted Stock high → low; zebra `#F8FAFF` on odd rows):
  * Rank = i+1 (bold `#1A4FAF`, centred); Colour = `col.toUpperCase()` (mono, bold 12px);
  * Stock = fmt(`stk`); Sale = fmt(`s45`); Stock Mix% = `stkmix`+"%"; Sale Mix% = `mix`+"%";
  * STR ROS = `strRos`+"d" or "—"; mono bold, colour `strColor(strRos, paint.eff)`.
  * Per month `mdata[j]`:
    * Pending PO = fmt(`pend`), grey `#9CA3AF`, left border 2px `#F1F5F9`;
    * Today's approved = fmt(`appr`) purple `#7C3AED` bold when `appr>0`, else "—" in `#CBD5E1`; tooltip
      "Approved today in Tool 1, not yet in F2";
    * Max Permissible: a badge (radius 5, bold 10px mono, dotted underline, `cursor:help`):
      * `action=="Watch List"` → "👁 Watch List", bg `#FFF7ED`, text `#92400E`, tooltip = row `watchReason`;
      * else bg `#DCFCE7`/text `#00873E` when `action=="Raise"`, else bg `#FEF2F2`/text `#DC2626`;
        text = fmt(`rec`) when `rec>0`, else the `action` ("Hold" / "No data"); tooltip = `mdata[j].tip`.
* Footer box (`#EAF0FB`, 11px, `#1A4FAF`): "💡 " + `paint.summary`
  ("{raiseCount} colours have headroom across selected months. Total recommended units: {fmt}. EffT={eff}d (T={T}d + {buf}% buffer).";
  the colour count and the total are bold).

## 5. Body — Multi Concept (`view.renderEngine == "A"`, data `view.a` : `RecAResultDto`)

* If `a.err` → `.rec-empty`, red `#DC2626`, max-width 500, text "⚠ " + `a.err` (`paint.emptyIsError`).

### 5.1 Step 1 — Attribute STR Ranking

* Heading row (flex): "Step 1 — Attribute STR Ranking · Dimension: **{a.dim}**" + a `<select>` (11px, bold
  `#1A4FAF` on `#F5F8FF`, border `#C7D7F0`, tooltip "Switch dimension — exploration only, does not change this
  row's actual decision") with `view.dimOptions` (`label`, `selected`; "(Relevant)" marks the Attribute Relevance
  Master dimension). On change → re-POST with `dimOverride = value`.
* Table header row 1: `{a.dim}` · Stock Mix% · Sale Mix% (small "(Last 45D)") · Deviation% · per month m_i a cell
  with colspan 1 (i = 0) or 2 (i > 0) = "MM.YYYY". Row 2: 4 empty, then i = 0: "Pending PO"; i > 0: "Attr Op STR" · "Pending PO".
* One row per `a.attrs[k]` (already sorted by Deviation% high → low; zebra):
  * Attribute = `av` (bold) + a tiny "▾"/"▲" button (tooltip "Show month-by-month breakdown") toggling the
    trace row below;
  * Stock Mix% = `stkmix`+"%"; Sale Mix% = `smix`+"%";
  * Deviation% = (`dev>=0` ? "+" : "") + `dev` + "%", bold, `RecommendationColors.DevColor(dev)` (< −5 red
    `#DC2626`, > 5 green `#00873E`, else amber `#D97706`);
  * per month i: i = 0 → fmt(`amdata[0].pend`) grey; i > 0 → **Attr Op STR = `amdata[i-1].str`** (the STR anchored
    to the PREVIOUS month — "-1 shift") shown as `{str}d` bold, colour `strColor(str, paint.eff)`, or "No data"
    grey when `amdata[i-1]` is missing / `str` null; then fmt(`amdata[i].pend`) grey.
* Trace row (hidden, `#FFFDF5`, colspan = 4 + Σ(i=0 ? 1 : 2)) — see §6 with `amdata`.
* **Total** row (`#EEF2FF`, bold, top border 2px `#C7D2FE`) from `a.bucketSTR`: "Total" + ▾ button ·
  "100.00%" · "100.00%" · "0.00%" · per month as above from `bucketSTR.amdata` (STR cells without bold,
  pending in `#6B7280`) + its trace row.

### 5.2 Step 2+3 — Colour × GM Recommendations

* Heading `paint.step2Heading`: "Step 2+3 — Colour × GM Recommendations (Q1 → Q2, BSR-validated only)".
* No recs → `.rec-empty` with `paint.noRecsMessage` (a line break before "Check:").
* Else a toolbar: three toggle chips (client state, default all closed; label prefix "▾" when open else "▸";
  bg `#EAF0FB` open / `#F8FAFF` closed): "Colour / Attribute Stock Qty" (showStockGrp), "Option PO Qty"
  (showOptionPO), "GM Description / Vendor" (showGMDetail); right-aligned green `#00873E` button
  "⬇ Download Excel (all columns)" → `POST …/export`.
* Header row 1: Attribute · Colour · Stock Mix% (within attr) · Sale Mix% (Last 45D, within attr) · Deviation% ·
  Gen Mat Recom · [GM Description · Vendor Cd · Vendor Name] · BSR Q · PDSTR%/d · Priority · Option Stock Qty ·
  [Colour Stock Qty · Attribute Stock Qty] · per month a cell colspan (i=0 ? 1 : 3) + (showOptionPO ? 1 : 0).
  Row 2 per month: i = 0 "Colour PO Qty" [+ "Option PO Qty"]; i > 0 "Attr Op STR" · "Col Op STR" ·
  "Colour PO Qty" [+ "Option PO Qty"]. Bracketed groups only when their toggle is open.
* Rows = `a.recs` in order, laid out with `paint.recLayout[i]`:
  * `sectionHeader` ≠ null → a full-width row before it (`#1E2A55`, white, bold 11.5px): "Q1 — Priority 1
    (best-quartile peers)" / "Q2 — Priority 2 (second-quartile peers)".
  * Row bg = `AttrTint[attrIndex % 5]`, left border 4px `AttrAccent[…]`, bottom border 2px accent when
    `isLastInRun` else 1px white (`RecommendationColors`).
  * Attribute cell only when `isFirstInRun`, `rowspan = span`, a chip (accent bg, white bold 10.5px) with `attr`.
  * Colour = `col.toUpperCase()` + ▾ button (tooltip "Show how this colour's STR is derived") toggling the colour
    trace row.
  * Stock Mix% = `colStockMixInAttr`%, Sale Mix% = `colMixInAttr`%, Deviation% = ±`colDev`% coloured as Step 1.
  * Gen Mat Recom = `gm` (mono 10px) + a hover-only link "Qty ▾" (tooltip "Show recommended qty for this option,
    by month") toggling the qty row (link turns solid blue, text "Qty ▲" while open).
  * [GM Description `gmDesc`|"—", Vendor Cd `vendorCd`|"—", Vendor Name `vendorName`|"—"].
  * BSR Q badge `q` (Q1: `#DCFCE7`/`#00873E`, else `#EAF0FB`/`#1A4FAF`); PDSTR%/d = `pdstr`+"%";
    Priority badge `priority` (P1 green, P2 blue, same colours).
  * Option Stock Qty = fmt(`optionStockQty`); [fmt(`colStockQty`), fmt(`attrStockQty`) in `#6B7280`].
  * per month i: i = 0 → fmt(`mdata[0].pend`) [+ fmt(`optPend`)]; i > 0 → Attr Op STR = `attrAmdata[i-1].str`,
    Col Op STR = `mdata[i-1].str` (both "-1 shift", `{str}d` bold coloured by `strColor`, "No data" grey when
    null), then fmt(`mdata[i].pend`) [+ fmt(`mdata[i].optPend`)].
* After the LAST row of each run, the buffered hidden rows of that run (per rec: colour trace, then qty row):
  * Colour trace (`#FFFDF5`): header line `recLayout[i].traceHeader`
    ("BLK — Option Stock Qty … units (this GM+Colour) · Colour Stock Qty … units · Sale Mix% (of Slim's 45D sales) = …% · Stock Mix% (of Slim's stock) = …%",
    colour code bold) + §6 mini-ledgers from `mdata`.
  * Qty row (`#EAF0FB`): header `recLayout[i].qtyHeader` (gm and colour bold) and a table Month · Recommended Qty ·
    Eff. Threshold · Daily Rate · Window Start · Formula, one row per `mdata`:
    no data (`str` null or `effExact` null) → "No data" / "—" …; else fmt(`rec`) (green `#00873E` if > 0, else
    `#6B7280`), `round(effExact,2)`+"d", `dr`+"/d", fmt(`winStockStart`), and `formula` (9.5px grey).
    Footnote (9px grey): "Each month's Recommended Qty is computed independently against today's actual
    stock/PO/forecast — it does not assume an earlier month's recommended qty here was actually raised."
* Footer box: "💡 " + `paint.summary` ("{n} Colour×GM combinations recommended. P1 (Q1): {p1} · P2 (Q2): {p2}. Total units: {fmt}. EffT={eff}d.").

## 6. Mini-ledger (`_traceRow`, colour trace, caution box)

For each entry `md` with a non-empty `chain` (one block, min-width 260px, wrapping flex, gap 16px):
"Anchor {md.mon}" (10px bold `#1A4FAF`) and a 10px table Month · Open · +PO · −Fcst · =Close over
`chain[0..3]`: Month = `mkey` (+ " (a)" on row 0), Open = fmt(`openStock`), +PO = fmt(`poIn`) green `#00873E`,
−Fcst = fmt(`colPlan`) amber `#D97706`, =Close = fmt(`closeStock`) bold; the row whose index = `d1Idx` (the
window start) is highlighted `#FFFBEB`. Below: "Window Start {fmt(winStockStart)} ÷ Daily Rate {dr} = **{str}d**"
("—" when `str` is null).

## 7. STR caution box (Tool 1 result card, `StrCautionDto`)

Shown under the decision reason when Tool 1 returns `strCaution`: amber box (`#FFFBEB`, border `#FDE68A`, left
4px `#D97706`, text `#78350F`), title "⚠ CAUTION — STR", one line per item = `items[k].text` + a small
"▾ Show calculation" / "▲ Hide calculation" toggle revealing the §6 ledger of `items[k].cs` (window-start row
`#FEF3C7`) and the footer `items[k].footer`.

## 8. Colours (`RecommendationColors`)

`StrColor(str, eff)`: null → `#9CA3AF`; `str <= eff` → `#00873E`; else `#DC2626` — **eff is the unrounded
`paint.eff`** (T1 × (1 + strBuffer/100)), not the rounded `eff` shown in texts.
`DevColor(dev)`: < −5 `#DC2626`, > 5 `#00873E`, else `#D97706`. Attribute palette: `AttrTint`, `AttrAccent`, `AttrText`.

## 9. Excel export

Five sheets, exactly as `RecommendationExportDto.sheets` (names, header row, column order, widths in characters):
`1-Recommendations`, `2-All Colours (full bucket)`, `3-All Gen Materials`, `4-Pending PO Detail (raw)`,
`5-Q1-Q2 Selection Trace`. Always every column, regardless of the on-screen toggles.

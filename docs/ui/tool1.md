# Tool 1 — "✏️ New Request" (Category view): UI spec

This is the original `index.html` Category form (`formGMInput`, `formPopulateColours`, `populateMonths`,
`formSubmit`, `renderResult`, `renderCatHistory`) plus `shared.js` (`saveRequest`, `approvalBadge`). The
**server makes the decision**. The portal only collects the fields, shows the pop-up and lays out the DTOs.
Every text and figure below comes from the API, already formatted (en-IN grouping, `toFixed`, …).

| Endpoint (roles buyer, cathead, planner, admin) | DTO |
|---|---|
| `PO_TOOL1_OPTIONS` → `POST api/v1/Tool1Options` body `{Gm}` | `Tool1FormOptionsDto` |
| `PO_TOOL1_VALIDATE` → `POST api/v1/Tool1Validate` body `Tool1RequestInputDto` | `Tool1ResultDto` |
| `PO_TOOL1_SUBMIT` → `POST api/v1/Tool1Submit` body `Tool1SubmitInputDto` | `Tool1ResultDto` |
| `PO_TOOL1_TODAY` → `POST api/v1/Tool1Today` body `{Mine}` | `Tool1TodayDto` |

All responses use the `ApiDataResponse<T>` envelope. A 400 `responseMessage` is a toast:
"Please fill all 4 fields." or "Upload required files first: f4, bsr".

## 1. The page

- Title **New PO Request**. Subtitle: "Enter article details to validate a PO request before raising."
- Right-hand counter: "Session: n", where n = `Tool1TodayDto.Count` (all of today's requests).
- Card header: "New Request", with a "Category" badge.
- Form grid with 4 fields:
  1. **Generic Material Code**: text input, monospace, placeholder "e.g. 1130153468". On every input, call
     `options?gm=` (debounce about 250 ms).
  2. **Colour**: a select. Its first, empty option is `ColourPlaceholder`. Then come the `Colours`
     (value/label). When `NewColourLabel` is set, the last option is value `__new__` with that label.
     - Choosing `__new__` shows a text box ("Type colour code...", upper-cased as typed). Choosing any
       other option hides the box and clears it.
     - When `ShowManualColour` is true, show the box straight away.
     - When the GM has fewer than 3 characters, the select holds only "— enter material first —".
  3. **Delivery Month**: a select. The first option is "— select month —", then `Months` (18 months from
     the server date, labels like "Oct 2026" or "Sept 2026"). Fill it from any `options` call.
  4. **Proposed Qty (units)**: a number input, min 1, placeholder "e.g. 10000".
- **Attribute strip**. Show it only when `ShowStrip` is true. It has 6 items: Brand Type, Division, MC CD,
  MC Desc, MRP, Flag, taken from `Strip.*` ("—" when blank; MRP is shown as "₹399").
- Buttons:
  - **✓ Validate Request** (primary). While the request runs, disable it and show a spinner with
    "Analyzing…".
  - **✕ Clear** (`formClear`) resets everything: GM, colour (back to "— enter material first —", text
    box hidden and emptied), qty, month, strip, the results area and the empty state.
- Empty state (shown until the first result): "No requests validated yet" / "Enter a Generic Material code and
  colour above to validate your first PO request."
- `MissingFiles` is not empty → Validate will refuse; you may disable the button and say which files.

## 2. Validate → (pop-up) → Submit

v1.1.4 did everything in one click. The server splits it into two calls, so the UI must do this:

1. `POST validate` with `{gm, colour, colourText, month, qty}`. Send `colour` = the select value (`__new__`
   when typing) and `colourText` = the text box. The server applies `formGetColour`.
2. If `NeedsConfirmation` is true, show the **Confirm Final Quantity** pop-up (`qty-confirm-overlay`):
   - Title: "Confirm Final Quantity".
   - Text: "Max Permissible headroom is available beyond (or different from) what you proposed. Choose the
     quantity to actually raise."
   - Two figures: "You proposed" = fmt(`ProposedQty`), "Max Permissible" = fmt(`MaxPermissibleQty`).
   - Label "Final quantity to raise (max {fmt(MaxPermissibleQty)})". Number input with min 0,
     max = `MaxPermissibleQty`, pre-filled with `MaxPermissibleQty`. While the user types, clamp it to
     0…max (`qcClamp`).
   - Buttons: **✕ Don't Raise** → `Declined: true`. **✓ Confirm Quantity** → `ConfirmedQty` = the input. The
     server clamps it again: integer part, 0…max.
   - The pop-up appears for **every** Single Concept result with Max Permissible > 0. That includes Auto Allow
     (you may raise up to the headroom) and the reduce case.
3. `POST submit` with the same fields plus `SeenSeq` (from Validate) and either `ConfirmedQty` or
   `Declined`. With no pop-up, submit right away with just `SeenSeq`.
   - Submit re-reads today's ledger and decides again at the confirmed quantity, then saves.
   - If someone approved against the same colour in between, the server hits the v1.1.4 409 path. It
     re-checks the headroom, caps the approved quantity and saves with force. The result then has
     `HeadroomChanged`, `ConflictByOthers`, the capped `FinalApprovedQty`, and a reason that starts
     "Headroom changed while you were confirming — …".
   - If Submit returns `Status = "needsConfirmation"` (no answer was sent), show the pop-up again.
4. Show every entry of `Toasts` in order. Examples:
   - "⚠ Another request for this colour was approved first — your approved qty was capped to 1,200"
   - "Watch List — sent to Cat Head for approval" (or "… the admin (no category head mapped) …")
   - "⚠ Request decided but NOT saved to server (HTTP 403)"
5. Render `Card` at the **top** of the results area, newest first. Refresh Today's Requests.

`Status`:
- `decided`: Validate result with no pop-up needed. Call Submit.
- `needsConfirmation`
- `saved`
- `notSaved`: the card is still shown, with the toasts.

## 3. Result card (`Tool1CardDto`)

### Header

- Circle with `Id`.
- Title line:
  - GM in monospace.
  - Colour chip (`#E8EDF5`).
  - Engine chip with `EngineLabel` on `EngineBg` / `EngineColour`:
    - "Single Concept MRP · Colour STR" `#E0F6FF`/`#006B99`
    - "Multi Concept MRP · Peer BSR" `#EAF0FB`/`#1A4FAF`
    - "New Product" `#F5F3FF`/`#7C3AED`
  - `OptionTypeChip` when it is not null: "True Repeat", "BOBs" or "New Option".
- Sub line: `SubLine`.
- Decision pill on the right: `Decision`, text `DecisionColour`, background `DecisionBg`, border
  `DecisionBorder`:

| Decision | text / bg / border |
|---|---|
| Auto Allow, Allow | #00873E / #DCFCE7 / #86EFAC |
| Recommend Reduced Qty | #D97706 / #FFFBEB / #FDE68A |
| Watch List | #854F0B / #FFF3CD / #FCD34D |
| Hold — Don't Raise, Block | #DC2626 / #FEF2F2 / #FECACA |
| Cannot Validate (and others) | #6B7280 / #F5F5F5 / #D1D5DB |

### Attribute chips

`Chips` is one row of small "KEY / value" pairs separated by thin dividers:
- Brand Type, Division, MC, MRP, Flag, Del. Month, Proposed Qty.
- Plus **Raised Qty** (value in pink `#E8006E`) when a quantity was confirmed.

### Single Concept (`Card.B`)

- `MixFailed` → show only the amber banner `MixBanner` (`#FFF3CD`, left border `#D97706`). No KPIs, no steps.
- Otherwise:
  - **KPI cards** (`Kpis`, value in `Colour`):
    - Op Stock, Mix %, Daily Rate, Colour STR (red when it breaches), Max Permissible (pink).
    - Plus "Max Permissible (After Raising PO)" (purple) when a quantity was confirmed.
  - **Toggle button**: collapsed by default with `ShowStepsLabel`; when open, `HideStepsLabel`.
  - Five step panels inside the toggle. Each has a coloured header with a "STEP n" badge and its `Title`.
- **STEP 1** (blue `#1A4FAF`):
  - Text: "F1 stock is uploaded as-of-today… All rows in F1 matching KEY-3 = Brand × MC × MRP × Flag × Colour
    are summed (multiple rows exist for different RDC/warehouse locations)."
  - Box "F1 Op Stock (SUM all rows)" = `OpStock` (hidden when it is null).
  - Box "KEY-3 Used" = `Key3`.
- **STEP 2** (green `#00873E`):
  - Text: "Mix % = Colour total sales ÷ MC total sales {Basis}. This tells us what share of the MC's total
    demand belongs to this colour. Used to split the F4 MC-level forecast down to colour level."
  - 4 boxes: "Colour Avg Sales/{Per}" `ColAvg`, "MC Total Avg Sales/{Per}" `McAvg`,
    "Colour Mix % = Col ÷ MC" `MixPct`, and "Min Mix% Threshold" `Threshold` (red unless `ThresholdOk`).
- **STEP 3** (cyan `#0891B2`):
  - Text: "Starting from F1 Op Stock (as-of-today), we roll forward month by month adding existing F2 POs and
    subtracting colour demand (F4 × Mix%). Current month uses only remaining days demand (F1 already reflects
    elapsed days). Future months use full month demand. Proposed PO of {ProposedQty} units lands in the
    delivery month."
  - Table columns: Month | Open Stock | + Existing PO (F2) | + Proposed PO | − Colour Demand | = Close Stock |
    Note. One `Rows` item per row:
    - Month cell = `Month` + `Tag` (small: amber for "(current month)", pink for "← Delivery Month", blue
      for "← Window Start"). Bold when `Bold`.
    - Demand = `Demand` + small amber `DemandNote`.
    - Note in `NoteColour`. Row background `Background`.
    - Rows with `PreRoll` are the months before delivery.
  - No rows → "No chain data available".
- **STEP 4** (purple `#7C3AED`):
  - Text: "From Day 1 of the Delivery Month, we look ahead D={D} days and compute total colour demand
    (F4 × Mix%) across those days. This gives us the Daily Rate. STR tells us how many days the opening stock
    of the Delivery Month will last at that daily rate."
  - Table (only when `Rows` is not empty): Month in Window | Colour Demand (F4×Mix%) | Days in Month |
    Days Taken | Period Demand.
    - Partial days are bold amber.
    - Last row is TOTAL: `TotalLabel` | "" | D | D | `TotalDemand`.
  - Box "STR Formula": `Formula` (5 monospace lines). The STR figure and the result are in `StrColour`.
- **STEP 5**: header `HeaderColour`, panel `PanelBg`.
  - White monospace box with the 4 `Formula` lines. The last line's figure is in `ResultColour`.
  - Then `VerdictLead` in bold `VerdictColour`, followed by `VerdictText`.
  - `Verdict` is HOLD, ALLOW or REDUCE.

### Multi Concept (`Card.A`)

- **Step 1 — Article & Peer Pool**: 5 tiles from `Step1`. Values are monospace in `Colour`. Keys are tiny
  upper-case labels and contain line breaks (`\n`).
- **Step 2 — Per-Day Sell Through% vs Peer Average** (when `Step2` is not null): 4 tiles.
- **Step 3 — {Heading}** (when present):
  - Line: `PoolText` left, `RankText` right (in `RankColour`).
  - Rank bar with lines at 25/50/75 % and a dot at `Position` %.
  - Labels under the bar: Q4 Slow, Q3 Watch, Q2 Healthy, Q1 Top.
- **Step 4 — Border Zone Check (PDSTR Gap)** (when present):
  - Monospace box with the `Lines`.
  - Strip with `Verdict`: green when `InBorderZone`, else red.
- `AbsoluteBlockNote`: red strip "⚡ Absolute Block Override: …".

### Bottom of every card

- **Reason box**: background / border in the decision colours, left border `DecisionColour`. Title
  `ReasonTitle` (small caps), then `Reason`.
- **STR caution** (`StrCaution`, Multi Concept Q1/Q2 Allow only): a separate amber box, never inside the
  green reason. It uses the same component as View Recommendation: `Items[].Text`, with an expandable
  breakdown that has `Footer`.
- **Footer**: `FooterHint` on the left, then the **📊 View Recommendation** button. It opens the
  recommendation modal with `RecEngine` / `RecInfo` (`PO_RECOMMENDATION_GET` with `{gm}` works too).

## 4. Today's Requests (`🕒 Today's Requests` tab)

- Page title "🕒 Today's Requests". Subtitle: "PO validation requests raised today. Quantities approved today
  for Single Concept MRP colours also reduce Max Permissible headroom for further requests against the same
  colour today."
- Empty: 🕒 "No requests yet", and a **New Request →** button.
- Table header bar:
  - "Today's Requests — all users".
  - Checkbox **Only mine**, which calls `today?mine=true`.
  - **Clear All** (staff only). It uses the platform's clear endpoint, with v1.1.4's confirm text.
- Columns, from `Tool1HistoryRowDto`, newest first:

| Column | Field |
|---|---|
| Request ID | `RequestId` |
| Generic Material | `Gm` |
| Colour | `Colour` |
| MC Desc | `McDesc` |
| MRP | `Mrp` |
| Del. Month | `Month` |
| Proposed Qty | `Qty` |
| Engine | tag `EngineTag` with class `EngineClass` |
| Option Type | `OptionType` |
| Decision | pill `Decision` with class `DecisionClass`, plus the `ApprovalBadge` under it: `Text` in `Colour` on `Background`, tooltip `Title` |
| Approved Qty | `ApprovedQty`, bold monospace |
| By | `By` |

- Badge texts: "⏳ Awaiting {names|admin}", "✓ Approved by X · 1,200", "✕ Rejected by X",
  "⊘ Cleared by admin – no longer waiting".
- Visibility:
  - Staff (planner/admin) see everything.
  - Others see their own requests and the ones they approve.
  - v1.1.4 also showed a restricted user the requests on their SCR-mapped MCs (`Shared.visibleRequests`).
    That needs the user's scope keys, which `AppUserContext` doesn't carry. Use the platform's
    `PO_REQUESTS_GET` listing if it applies the scope, or extend `GetTodayAsync` once a scope provider
    exists.
- Poll every 30 s, or refresh after each submit.

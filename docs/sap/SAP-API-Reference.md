# VMM PO Governance — SAP Interface API Reference (v1.4)

**For:** SAP ABAP / workflow / Basis developers building the SAP side of the PO Governance check.
**Contract:** *SAP API Specification v1.4* and `po-governance-sap-api.yaml` (OpenAPI 3.0) in
`legacy/python-app/docs/`. This page lists every endpoint with its request and response as the tool answers them
today. All examples below were captured from the running interface.

---

## 1. Connection

| Item | Staging (SAP QA) | Production |
|---|---|---|
| Base URL | `http://192.168.0.175:8178/api/sap/v1` | `https://<vm-dns-name>:8443/api/sap/v1` (company certificate) |
| SM59 destination | Type G, e.g. `ZPOGOV_API_TEST` — host `192.168.0.175`, port `8178`, path prefix `/api/sap/v1`, SSL off | Type G `ZPOGOV_API` — port `8443`, SSL active (import the company CA chain in STRUST) |
| Technical user | `sap_ecq` (one per SAP system) | `sap_ecp` |
| Connection test | `GET /ping` → `200` | same |

### Authentication

- **HTTP Basic authentication** with a technical user (SM59 → *Logon & Security* → *Basic Authentication*). The tool's
  administrator creates the user and gives you the password, generated with 32 characters, through a secure channel.
- Wrong or missing credentials → `401 UNAUTHORIZED`, with header `WWW-Authenticate: Basic`.
- After 5 wrong passwords in a row the user is locked for 15 minutes → `401 USER_LOCKED`.
- An optional IP allow-list can be switched on by the tool's administrator. A caller outside the list → `401 IP_NOT_ALLOWED`.

### Rules for every call

- UTF-8 JSON, `Content-Type: application/json`. Field names are **snake_case**, exactly as written here.
- Timestamps are ISO 8601 with offset, e.g. `2026-10-22T14:24:58+05:30`. Dates are `YYYY-MM-DD`.
- Quantities are JSON numbers in the base unit (normally `PC`). Amounts are in document currency.
- Article numbers (`article`, `generic_article`) are sent in **SAP internal format with leading zeros**; the tool
  removes them.
- **OTB is never sent and never checked by the tool**; it stays in SAP.
- Every answer carries an `X-Trace-Id` header (e.g. `T-a1490d`). Quote it when you report a problem.

### Error body (every non-200 answer)

```json
{ "error_code": "VALIDATION_FAILED", "message": "purchase_order.items[0].order_qty must be > 0", "trace_id": "T-a19102" }
```

| error_code | HTTP | Meaning | What SAP does |
|---|---|---|---|
| `UNAUTHORIZED` | 401 | Missing / wrong user or password | Fix SM59 credentials |
| `USER_LOCKED` | 401 | Too many wrong passwords, or user disabled | Ask the tool admin to unlock |
| `IP_NOT_ALLOWED` | 401 | Caller IP not in the allow-list (only when switched on) | Ask the tool admin to add the IP |
| `INVALID_JSON` | 400 | Body is not valid JSON | Fix the program; do not resend unchanged |
| `VALIDATION_FAILED` | 400 | Mandatory field missing (`/decisions`: generic_article, colour, order_qty, delivery_date), wrong value, quantity ≤ 0, bad date (`message` names the field) | Fix the program; do not resend unchanged |
| `REQUEST_ID_CONFLICT` | 409 | `/decisions`: same `request_id` already used with a different body | Generate a new `request_id` |
| `CANNOT_DECIDE` | 422 | `/decisions`: an item cannot be decided (e.g. article unknown to the tool) | Treat as **NOT_CHECKED**, continue as today |
| `BATCH_TOO_LARGE` | 413 | `/events`: more than 500 events or 5 MB | Split the batch and resend |
| `REQUEST_NOT_FOUND` | 404 | `/documents`: no answered decision for the `request_id` | Use the `request_id` of a `/decisions` call that answered 200 |
| `DOCUMENT_CONFLICT` | 409 | `/documents`: another document is already stored for the `request_id` | Send `replace = true` if it is a correction |
| `DECISION_TIMEOUT`, `DATA_NOT_LOADED`, `DATABASE_UNAVAILABLE`, `SAP_INTERFACE_DISABLED`, `INTERNAL_ERROR` | 503 | Tool temporarily unable to answer | `/decisions`: **NOT_CHECKED**, continue as today. `/events`: retry |

---

## 2. Endpoints at a glance

| # | Method & path | Called by | Purpose |
|---|---|---|---|
| 1 | `GET /ping` | SM59 connection test; health job every minute | Connection and credential check |
| 2 | `GET /reason-codes` | SAP approver screen | Exception reasons for the dropdown |
| 3 | `POST /decisions` | Release workflow background step "PO Governance check" (after OTB + depth check passed) | **Governance verdict per PO item** (synchronous, SAP waits max 3 s) |
| 4 | `POST /events` | bgRFC queue from BAdIs (after commit) | PO, goods receipt / return and governance-outcome events (asynchronous) |
| 5 | `POST /documents` | After the PO is really created / received in SAP | **Final document number (MRN) against the `request_id`** of the decision — feeds the "PO generated" report |

---

## 3. `GET /ping`

Connection test. If 3 health checks fail in a row, SAP switches the governance step off and runs the previous process.

**Request:** no body; Basic authentication only.

**Response `200`:**

```json
{
  "status": "ok",
  "server_time": "2026-10-22T14:24:58+05:30",
  "api_version": "1.0.0",
  "environment": "TEST",
  "source_system_recognised": "ECQ"
}
```

| Field | Type | Meaning |
|---|---|---|
| `status` | string | Always `ok` when the answer is 200 |
| `server_time` | date-time | Tool server time |
| `api_version` | string | Interface version |
| `environment` | `TEST` \| `PRODUCTION` | Which copy of the tool answered |
| `source_system_recognised` | string | SAP system ID registered for the technical user (`ECQ`, `ECP`) — check it matches `SY-SYSID` |

Other answers: `401` (credentials), `503` (tool down).

---

## 4. `GET /reason-codes`

Exception reasons the approver can choose when asking for an exception (send the chosen `code` back in a `GOV_OUTCOME`
event). Maintained by Planning in the tool; read it when the screen opens or cache it daily.

**Request:** no body.

**Response `200`:**

```json
{
  "version": "2026-10-22",
  "reason_codes": [
    { "code": "NEWL", "text": "New launch - no sales history", "active": true },
    { "code": "FEST", "text": "Festive / event build-up", "active": true },
    { "code": "VMOQ", "text": "Vendor minimum order quantity", "active": true },
    { "code": "PRCA", "text": "Price advantage / one-time buy", "active": true },
    { "code": "REPL", "text": "Store replenishment gap", "active": true },
    { "code": "OTHR", "text": "Other (note mandatory)", "active": true }
  ]
}
```

| Field | Type | Meaning |
|---|---|---|
| `version` | string | Date of the last change — refresh your cache when it changes |
| `reason_codes[].code` | string, max 4 | Code to show / store |
| `reason_codes[].text` | string, max 60 | Text to show |
| `reason_codes[].active` | boolean | `false` = do not offer any more |

The list above is the proposal from the specification and is **still to be confirmed by the business**.

---

## 5. `POST /decisions` — the verdict API

Called by the workflow background step **after SAP's OTB check and depth plan check have passed**. SAP sends the PO;
the tool answers with a verdict per item.

### Timing and fallback contract

- The tool answers in **~20–60 ms** (measured; 50 parallel POs all under 1 s). SAP waits **at most 3 seconds**.
- On timeout, network error or **any non-200 answer**, SAP marks the PO **`NOT_CHECKED`** and continues the workflow
  exactly as today. **The tool can never stop SAP.**
- **Mode:** send `SHADOW` for now. The tool decides and logs, but SAP must **not** route on the verdict. `LIVE` = SAP
  routes on `route_to`. Control it in SAP, e.g. switch `ZPOGOV_ACTIVE` per division.
- **Idempotency:** `request_id` identifies one check (e.g. work-item ID + PO + version). Repeating the same `request_id`
  with the same body returns the **same answer** (same `decision_id`). The same `request_id` with a different body →
  `409 REQUEST_ID_CONFLICT`. If SAP leaves `request_id` out, the tool builds one (`AUTO-yyyyMMdd-<body hash>`), so the
  same body sent again on the same day also returns the same answer.

### Request

**Business rule: only four fields per item are mandatory** — `generic_article`, `colour`, `order_qty` and
`delivery_date` (Generic Material + Colour + Delivery Month + Proposed Qty). Everything else is optional. The smallest
valid request:

```json
{
  "purchase_order": {
    "items": [
      { "generic_article": "000000001101000101", "colour": "WHT", "order_qty": 50000, "delivery_date": "2026-11-15" }
    ]
  }
}
```

The full request, with every optional field SAP may send for audit and reporting:

```json
{
  "request_id": "WI000004711-4500099001-V1",
  "mode": "SHADOW",
  "source_system": "ECQ",
  "client": "300",
  "otb_check": "PASSED",
  "depth_check": "PASSED",
  "previous_decision_id": null,
  "requested_at": "2026-10-21T11:50:00+05:30",
  "purchase_order": {
    "po_number": "4500099001",
    "doc_type": "NB",
    "vendor": "0000201345",
    "purch_org": "1000",
    "purch_group": "B07",
    "buyer_user": "BUYER07",
    "buyer_email": "ravi.k@vishalwholesale.co.in",
    "division": "MENS",
    "currency": "INR",
    "items": [
      {
        "po_item": 10,
        "article": "000000001101000101",
        "generic_article": "000000001101000101",
        "colour": "WHT",
        "merch_category": "MC1021",
        "season": "AW26",
        "site": "DC01",
        "order_qty": 50000,
        "uom": "PC",
        "net_value": 418800.00,
        "delivery_date": "2026-11-15"
      }
    ]
  }
}
```

| Field | Type | Req. | SAP source / meaning | When left out |
|---|---|---|---|---|
| `purchase_order.items[]` | array, min 1 | **yes** | One entry per PO item | — |
| `items[].generic_article` | string | **yes** | `MARA-SATNR` (generic article), leading zeros kept | — |
| `items[].colour` | string | **yes** | Colour characteristic of the variant: Colour Master **code** preferred (description also accepted) | — |
| `items[].order_qty` | number, > 0 | **yes** | `EKPO-MENGE` in base unit (proposed quantity) | — |
| `items[].delivery_date` | date | **yes** | First schedule line `EKET-EINDT` — the tool uses its month | — |
| `request_id` | string, max 80 | no | Unique per check, e.g. work-item ID + PO + version | `AUTO-yyyyMMdd-<body hash>` |
| `mode` | `SHADOW` \| `LIVE` | no | See above | `SHADOW` |
| `source_system` | string | no | `SY-SYSID` | System of the technical user (`ECQ` / `ECP`) |
| `client` | string | no | `SY-MANDT` | — |
| `otb_check` | `PASSED` | no | Assertion that OTB passed | `PASSED` |
| `depth_check` | `PASSED` \| `BYPASSED` | no | `BYPASSED` only for a quantity reduced under a previous REDUCE decision | `PASSED` |
| `previous_decision_id` | string \| null | no | On resubmission after a REDUCE: the `decision_id` it was reduced under | — |
| `requested_at` | date-time | no | When SAP sent the check | — |
| `purchase_order.po_number` | string | no | `EKKO-EBELN` (recommended: shown in the audit log and matched with `GOV_OUTCOME`) | — |
| `purchase_order.doc_type` | string | no | `EKKO-BSART` | — |
| `purchase_order.vendor` | string | no | `EKKO-LIFNR` | — |
| `purchase_order.purch_org` | string | no | `EKKO-EKORG` | — |
| `purchase_order.purch_group` | string | no | `EKKO-EKGRP` | — |
| `purchase_order.buyer_user` | string | no | Buyer's SAP user | — |
| `purchase_order.buyer_email` | string \| null | no | Buyer's e-mail | — |
| `purchase_order.division` | string | no | Division | — |
| `purchase_order.currency` | string | no | `EKKO-WAERS` | — |
| `items[].po_item` | integer | no | `EKPO-EBELP` (recommended, so SAP can match the answer to its item) | 10, 20, 30 … in the order sent |
| `items[].article` | string | no | `EKPO-MATNR` (variant); used only when the generic article is not known to the tool | — |
| `items[].merch_category` | string | no | `EKPO-MATKL` | — |
| `items[].season` | string | no | Season | — |
| `items[].site` | string | no | `EKPO-WERKS` (informational; the tool works at company level) | — |
| `items[].uom` | string | no | `EKPO-MEINS` | — |
| `items[].net_value` | number | no | Item net value | — |

An optional field that **is** sent must still be valid (e.g. `mode` must be `SHADOW` or `LIVE`, `po_item` must not
repeat), otherwise the answer is `400 VALIDATION_FAILED`. The answer always echoes `request_id`, `mode` and every
`po_item`, including the ones the tool filled in.

**How the tool reads an item:**
- **Article:** the generic article with its leading zeros removed (`000000001101000101` → `1101000101`), looked up in
  the tool's Article Master / Universe Master (then `article`, if sent).
- **Colour:** matched by Colour Master code first, then by description.
- **Month:** the delivery month is taken from `delivery_date`. A date in the past counts as the current month.
- **Size variants:** items of one PO with the same generic article, colour and delivery month are decided **together**
  on their summed quantity. A REDUCE `max_qty` is split over them in proportion to their quantities.

### Response `200`

```json
{
  "decision_id": "D-20261022-000002",
  "request_id": "WI-REDUCE-0001",
  "mode": "SHADOW",
  "overall_verdict": "REDUCE",
  "verdict_code": "R",
  "route_to": "BUYER",
  "bm_head_required": false,
  "summary_text": "Item 10: reduce to 15,928 - Stock + pending + this PO cover 265 days, above the 79-day norm; max 15,928 pcs",
  "rules_version": "R-P1",
  "data_delayed": false,
  "data_as_of": { "bw_snapshot": "2026-10-22T14:24:36+05:30", "events_lag_seconds": 0 },
  "decided_at": "2026-10-22T14:24:58+05:30",
  "view_url": "http://192.168.0.175:8177/verdict/R_LVm2VaqeZN94W6EOgf44SuA27lJ9VQr5rUyQx5hb0",
  "view_url_expires_at": "2027-01-20T14:24:58+05:30",
  "items": [
    {
      "po_item": 10,
      "verdict": "REDUCE",
      "verdict_code": "R",
      "max_qty": 15928,
      "reason_code": "COVER_ABOVE_NORM",
      "reason_text": "Stock + pending + this PO cover 265 days, above the 79-day norm; max 15,928 pcs",
      "rule_id": "COVER-01",
      "metrics": { "plan_qty": 6303, "stock_qty": 2707, "open_po_qty": 2703, "headroom_qty": 15928, "cover_days": 265 }
    }
  ]
}
```

| Field | Type | Meaning / what SAP does with it |
|---|---|---|
| `decision_id` | string | e.g. `D-20261022-000002`. **Store it on the PO / workflow container** for audit and send it back in `GOV_OUTCOME` events |
| `request_id` | string | Echo of the request |
| `mode` | `SHADOW` \| `LIVE` | Echo of the request |
| `overall_verdict` | `ALLOW` \| `REDUCE` \| `WATCH` \| `HOLD` | Most severe item verdict (HOLD > WATCH > REDUCE > ALLOW) |
| `verdict_code` | `A` \| `R` \| `W` \| `H` | One-character code for SAP domain values |
| `route_to` | `NONE` \| `BUYER` \| `CATHEAD` \| `BMHEAD` | Where the workflow goes in LIVE mode (ignored in SHADOW). `NONE` = continue normal approvals |
| `bm_head_required` | boolean | Escalation to the B&M head (business rule pending — currently always `false`) |
| `summary_text` | string, max 255 | Short text for the work item / PO header note |
| `rules_version` | string | Version of the planner parameters used |
| `data_delayed` | boolean | `true` = the tool's event feed is behind the agreed limit → **SAP continues the PO as today (NOT_CHECKED)** |
| `data_as_of.bw_snapshot` | date-time | Time of the stock data the decision used |
| `data_as_of.events_lag_seconds` | integer | How far behind the event feed is |
| `decided_at` | date-time | When the tool decided |
| `view_url` | string (URL) | **Link to the verdict page** on the PO Governance portal. Show it to the buyer / approver (work item, PO note, e-mail): clicking it opens the verdict with its items and reasons — read-only, **no sign-in needed**. The token in the link is random and cannot be guessed; anyone who has the link can open it, so keep it inside the company. Omitted if the link is switched off |
| `view_url_expires_at` | date-time | Until when the link works (90 days by default) |

**The verdict page** (`view_url`) shows the complete verdict: what happens next for this verdict, the decision details
(decision / request ID, decided at, route, mode, buyer, division, SAP system, rules version, stock data date, final
document), every item with its reason, rule and the figures behind it (plan, stock, open PO, headroom, cover days or
sell-through), totals, and a print button. A decision answered before the link existed gets its `view_url` the first
time SAP repeats the same `request_id` (the answer is otherwise unchanged and stays the same on every later repeat).
| `items[].po_item` | integer | PO item |
| `items[].verdict` / `verdict_code` | as above | Item verdict |
| `items[].max_qty` | number \| null | **Maximum allowed quantity when the verdict is REDUCE**; `null` otherwise |
| `items[].reason_code` | string | Machine-readable reason (table below) |
| `items[].reason_text` | string, max 132 | Text for the buyer / approver |
| `items[].rule_id` | string | Rule that produced the verdict |
| `items[].metrics` | object | Figures behind the verdict. Single Concept: `plan_qty`, `stock_qty`, `open_po_qty`, `headroom_qty`, `cover_days`. Multi Concept: `sell_through_pct`. Fields that do not apply are omitted |

`cover_days` is the cover from the first day of the month after delivery. It can be `0`: that means current stock
plus pending POs is sold out before the delivery window starts.

### Verdicts — what each one means for SAP

| Verdict | Code | `route_to` | SAP action (LIVE mode) |
|---|---|---|---|
| `ALLOW` | A | `NONE` | Continue normal approvals |
| `REDUCE` | R | `BUYER` | Buyer reduces the item to `max_qty` or less. Depth plan bypass: store `decision_id` + `max_qty` on the item; if the quantity is reduced to `max_qty` or less, resubmit with `depth_check = BYPASSED` and `previous_decision_id` |
| `WATCH` | W | `CATHEAD` | Category head approves or rejects (exception with reason code) |
| `HOLD` | H | `CATHEAD` | Do not release as is; category head decides. **A blocked article (slow seller) is sent as `HOLD` with `reason_code = BLOCK`** |

### Reason codes per verdict

| Verdict | reason_code | rule_id | Meaning |
|---|---|---|---|
| ALLOW | `COVER_WITHIN_NORM` | COVER-00 | Stock + pending + this PO within the cover norm |
| ALLOW | `NO_PLAN_DATA` | PLAN-00 | No sales plan for the bucket — allowed |
| ALLOW | `PEER_Q1` / `PEER_Q2` | PEER-Q1 / PEER-Q2 | Multi Concept: ranks in the top half of its peer group |
| ALLOW | `PEER_Q3` | PEER-Q3-OK | Q3 but at / near the peer average (border zone) |
| ALLOW | `BOBS_SIBLINGS_OK` | BOBS-01 | New colour of a selling style; sibling colours sell well |
| ALLOW | `LOW_HISTORY` | WOF-00 | Too little selling history to judge |
| ALLOW | `NO_PEER_POOL` | PEER-00 | No comparable styles |
| ALLOW | `NEW_ARTICLE` | NEW-00 | New article (not in the tool's history) |
| REDUCE | `COVER_ABOVE_NORM` | COVER-01 | Cover above the norm — reduce to `max_qty` |
| WATCH | `MIX_BELOW_MIN` | MIX-01 | Colour share below the minimum mix (broken / cut size likely) |
| WATCH | `PEER_Q3` | PEER-Q3-GAP | Q3, beyond the border zone |
| WATCH | `PEER_Q4` | PEER-Q4-WATCH | Q4 but at / near the peer average |
| WATCH | `BOBS_SIBLING_LOW` | BOBS-02 | A sibling colour sells poorly |
| HOLD | `COVER_NO_HEADROOM` | COVER-02 | Stock + pending already above the norm — no headroom (max 0) |
| HOLD | `BLOCK` | BLOCK-Q4 | Q4, well below the peer average: confirmed slow seller |
| HOLD | `BLOCK` | BLOCK-ABS | Absolute block: sell-thru below 1 %/day after 4+ weeks on floor |

The tool may later send `BLOCK` (code `B`) as its own verdict instead of `HOLD` + `BLOCK`, once SAP has added that
domain value. This is switched in the tool's settings and will be agreed with you first. Until then, handle
`reason_code = BLOCK` under `HOLD`.

### More examples (captured)

**ALLOW**, with the colour sent as its description (`"colour": "wht desc"`):

```json
{ "decision_id": "D-20261022-000001", "request_id": "WI-ALLOW-0001", "mode": "SHADOW",
  "overall_verdict": "ALLOW", "verdict_code": "A", "route_to": "NONE", "bm_head_required": false,
  "summary_text": "All 1 item(s) within the norms", "rules_version": "R-P1", "data_delayed": false,
  "data_as_of": { "bw_snapshot": "2026-10-22T14:24:36+05:30", "events_lag_seconds": 0 },
  "decided_at": "2026-10-22T14:24:58+05:30",
  "items": [ { "po_item": 10, "verdict": "ALLOW", "verdict_code": "A", "max_qty": null,
               "reason_code": "COVER_WITHIN_NORM", "reason_text": "Cover 0 days within the 79-day norm", "rule_id": "COVER-00",
               "metrics": { "plan_qty": 6303, "stock_qty": 2707, "open_po_qty": 2703, "headroom_qty": 15928, "cover_days": 0 } } ] }
```

**HOLD** (no headroom):

```json
{ "...": "...", "overall_verdict": "HOLD", "verdict_code": "H", "route_to": "CATHEAD",
  "summary_text": "Item 10: hold - Stock + pending already above the 79-day cover norm; no headroom (max 0)",
  "items": [ { "po_item": 10, "verdict": "HOLD", "verdict_code": "H", "max_qty": null,
               "reason_code": "COVER_NO_HEADROOM", "reason_text": "Stock + pending already above the 79-day cover norm; no headroom (max 0)",
               "rule_id": "COVER-02",
               "metrics": { "plan_qty": 7108, "stock_qty": 25604, "open_po_qty": 3698, "headroom_qty": 0, "cover_days": 101 } } ] }
```

**WATCH** (colour mix):

```json
{ "...": "...", "mode": "LIVE", "overall_verdict": "WATCH", "verdict_code": "W", "route_to": "CATHEAD",
  "summary_text": "Item 10: watch - Colour mix 0.0% below the 2% minimum of its MC-MRP",
  "items": [ { "po_item": 10, "verdict": "WATCH", "verdict_code": "W", "max_qty": null,
               "reason_code": "MIX_BELOW_MIN", "reason_text": "Colour mix 0.0% below the 2% minimum of its MC-MRP",
               "rule_id": "MIX-01", "metrics": { "stock_qty": 874, "open_po_qty": 0 } } ] }
```

**Block** (sent as HOLD + BLOCK):

```json
{ "...": "...", "overall_verdict": "HOLD", "verdict_code": "H", "route_to": "CATHEAD",
  "summary_text": "Item 10: hold - Ranks Q4, sell-thru 64.4% below the peer average: confirmed slow seller",
  "items": [ { "po_item": 10, "verdict": "HOLD", "verdict_code": "H", "max_qty": null,
               "reason_code": "BLOCK", "reason_text": "Ranks Q4, sell-thru 64.4% below the peer average: confirmed slow seller",
               "rule_id": "BLOCK-Q4", "metrics": { "sell_through_pct": 0.97 } } ] }
```

**Several items:** two size variants of one generic article and colour (decided together, `max_qty` split 9,557 + 6,371
= 15,928) plus a Multi Concept item:

```json
{ "decision_id": "D-20261022-000006", "request_id": "WI-MULTI-0001", "mode": "SHADOW",
  "overall_verdict": "REDUCE", "verdict_code": "R", "route_to": "BUYER", "bm_head_required": false,
  "summary_text": "Item 10: reduce to 9,557 - …; Item 20: reduce to 6,371 - …",
  "items": [
    { "po_item": 10, "verdict": "REDUCE", "verdict_code": "R", "max_qty": 9557, "reason_code": "COVER_ABOVE_NORM",
      "reason_text": "Stock + pending + this PO cover 265 days, above the 79-day norm; max 15,928 pcs for 2 items together (50,000 pcs ordered)",
      "rule_id": "COVER-01", "metrics": { "plan_qty": 6303, "stock_qty": 2707, "open_po_qty": 2703, "headroom_qty": 15928, "cover_days": 265 } },
    { "po_item": 20, "verdict": "REDUCE", "verdict_code": "R", "max_qty": 6371, "reason_code": "COVER_ABOVE_NORM",
      "reason_text": "Stock + pending + this PO cover 265 days, above the 79-day norm; max 15,928 pcs for 2 items together (50,000 pcs ordered)",
      "rule_id": "COVER-01", "metrics": { "plan_qty": 6303, "stock_qty": 2707, "open_po_qty": 2703, "headroom_qty": 15928, "cover_days": 265 } },
    { "po_item": 30, "verdict": "ALLOW", "verdict_code": "A", "max_qty": null, "reason_code": "PEER_Q1",
      "reason_text": "Ranks Q1 in its peer group (sell-thru 5.2%/day): healthy seller", "rule_id": "PEER-Q1",
      "metrics": { "sell_through_pct": 5.2 } } ] }
```

### Error answers of `/decisions` (captured)

| Case | HTTP | Body |
|---|---|---|
| Article unknown to the tool (whole PO not decided) | 422 | `{"error_code":"CANNOT_DECIDE","message":"Item 10: Article 9999999999 is unknown to the tool (not in Article / Universe Master, F2 or BSR)","trace_id":"T-a1490d"}` |
| Same `request_id`, different body | 409 | `{"error_code":"REQUEST_ID_CONFLICT","message":"request_id WI-ALLOW-0001 was already used with a different body (decision D-20261022-000001) - generate a new request_id","trace_id":"T-a4d243"}` |
| `colour` missing | 400 | `{"error_code":"VALIDATION_FAILED","message":"purchase_order.items[0].colour is required","trace_id":"…"}` |
| `order_qty` = 0 | 400 | `{"error_code":"VALIDATION_FAILED","message":"purchase_order.items[0].order_qty must be > 0","trace_id":"T-a19102"}` |
| Broken JSON | 400 | `{"error_code":"INVALID_JSON","message":"Invalid JSON: …","trace_id":"T-1e66c8"}` |
| No / wrong credentials | 401 | `{"error_code":"UNAUTHORIZED","message":"Missing or wrong credentials","trace_id":"T-809985"}` |

If **any** item cannot be decided, the whole request is answered `422`. The message names the item(s), and SAP treats
the PO as NOT_CHECKED. Every non-200 answer from `/decisions` = **NOT_CHECKED, continue as today**.

---

## 6. `POST /events`

Called from a **bgRFC unit after commit**. One call carries **1–500 events** in the order they occurred (ascending
`seq`).

### Delivery contract

- `200` = every event in the batch is safely stored (as accepted, duplicate or parked). **Remove the unit from the
  queue.**
- Any other status, or no answer within **10 seconds** → **retry the same batch**. Resending is always safe:
  events are de-duplicated by `msg_id`.
- An event that fails validation is **parked** on the tool side (listed in `parked`, alerted to IT) and **never
  blocks the queue**.
- **PO events carry the full current state of the PO** (header + all items), not only what changed.
- Scope: vendor POs and goods movements against vendor POs (receipts 101/102, returns 122/161). Stock transfers (STO)
  are not sent.

### Request

```json
{
  "source_system": "ECQ",
  "client": "300",
  "queue": "ZPOGOV_EVENTS",
  "sent_at": "2026-10-21T11:42:08+05:30",
  "events": [ { "...one or more events, see below...": "" } ]
}
```

| Field | Type | Req. | Meaning |
|---|---|---|---|
| `source_system` | string | **yes** | `SY-SYSID` |
| `client` | string | **yes** | `SY-MANDT` |
| `queue` | string | no | bgRFC queue name, e.g. `ZPOGOV_EVENTS` |
| `sent_at` | date-time | no | When the unit was sent |
| `events` | array, 1–500 | **yes** | The events |

**Every event:**

| Field | Type | Req. | Meaning |
|---|---|---|---|
| `msg_id` | string, max 80 | **yes** | Globally unique, built by SAP (system + client + object + timestamp / counter). Used for de-duplication |
| `seq` | integer (int64) | **yes** | Ascending number per queue, assigned when the event is written |
| `event_type` | enum | **yes** | One of the 11 types below |
| `occurred_at` | date-time | **yes** | When the document was saved in SAP |
| `sap_user` | string | no | `SY-UNAME` |
| `purchase_order` / `goods_movement` / `governance_outcome` | object | **one of them** | Filled according to `event_type` |

| event_type | Payload |
|---|---|
| `PO_CREATED`, `PO_CHANGED`, `PO_RELEASED`, `PO_RELEASE_REVOKED`, `PO_CLOSED`, `PO_DELETED` | `purchase_order` (full current state) |
| `GR_POSTED`, `GR_REVERSED`, `RETURN_POSTED`, `RETURN_REVERSED` | `goods_movement` (one material-document line) |
| `GOV_OUTCOME` | `governance_outcome` (what happened after a verdict) |

### `purchase_order` (PO events)

```json
{
  "msg_id": "ECQ300-PO-4500098765-20261021101502-01", "seq": 45098, "event_type": "PO_RELEASED",
  "occurred_at": "2026-10-21T10:15:02+05:30", "sap_user": "BUYER07",
  "purchase_order": {
    "po_number": "4500098765", "doc_type": "NB", "vendor": "0000201345", "purch_org": "1000", "purch_group": "B07",
    "created_on": "2026-10-18", "created_by": "BUYER07", "release_status": "RELEASED", "release_indicator": "R",
    "currency": "INR", "deleted": false,
    "items": [
      { "po_item": 10, "article": "000000001101000101", "generic_article": "000000001101000101", "colour": "WHT",
        "merch_category": "1101", "site": "DC01", "order_qty": 600, "received_qty": 0, "returned_qty": 0, "open_qty": 600,
        "uom": "PC", "net_price": 349.00, "price_unit": 1, "delivery_date": "2026-11-10",
        "deletion_flag": false, "delivery_completed": false }
    ]
  }
}
```

| Field | Req. | SAP source |
|---|---|---|
| `po_number` | **yes** | `EKKO-EBELN` |
| `doc_type`, `vendor`, `purch_org`, `purch_group` | no | `EKKO-BSART`, `LIFNR`, `EKORG`, `EKGRP` |
| `created_on`, `created_by` | no | `EKKO-AEDAT / BEDAT`, `ERNAM` |
| `release_status` | no | `NOT_RELEASED` \| `IN_RELEASE` \| `RELEASED` \| `REFUSED` (from `FRGKE` / `FRGZU`) |
| `release_indicator`, `currency` | no | `EKKO-FRGKE`, `WAERS` |
| `deleted` | no | `true` if the whole PO is deleted / cancelled |
| `items[]` | **yes** | All items |
| `items[].po_item`, `article`, `order_qty`, `uom` | **yes** | `EKPO-EBELP`, `MATNR`, `MENGE`, `MEINS` |
| `items[].generic_article`, `colour` | no (send them) | `MARA-SATNR`, colour characteristic |
| `items[].merch_category`, `site` | no | `EKPO-MATKL`, `WERKS` |
| `items[].received_qty`, `returned_qty`, `open_qty` | no | From `EKBE`; `open_qty = order − received + returned`, 0 if completed / deleted |
| `items[].net_price`, `price_unit` | no | `EKPO-NETPR`, `PEINH` |
| `items[].delivery_date` | no | First schedule line `EKET-EINDT` |
| `items[].deletion_flag`, `delivery_completed` | no | `EKPO-LOEKZ`, `ELIKZ` |

### `goods_movement` (GR / return events)

```json
{
  "msg_id": "ECQ300-GR-5000123456-2026-0001", "seq": 45121, "event_type": "GR_POSTED",
  "occurred_at": "2026-10-21T11:42:07+05:30", "sap_user": "DCUSER01",
  "goods_movement": {
    "material_doc": "5000123456", "doc_year": 2026, "doc_item": 1, "movement_type": "101", "debit_credit": "S",
    "posting_date": "2026-10-21", "article": "000000001101000101", "generic_article": "000000001101000101",
    "colour": "WHT", "site": "DC01", "storage_location": "0001", "quantity": 240, "uom": "PC",
    "po_number": "4500098765", "po_item": 10, "reversal_of": null
  }
}
```

Reversal (`GR_REVERSED`, movement 102) and return (`RETURN_POSTED`, movement 122):

```json
{ "msg_id": "ECQ300-GR-5000123457-2026-0001", "seq": 45130, "event_type": "GR_REVERSED", "occurred_at": "2026-10-21T11:50:00+05:30",
  "sap_user": "DCUSER01",
  "goods_movement": { "material_doc": "5000123457", "doc_year": 2026, "doc_item": 1, "movement_type": "102", "debit_credit": "H",
                      "posting_date": "2026-10-21", "article": "000000001101000101", "quantity": 40, "uom": "PC",
                      "po_number": "4500098765", "po_item": 10, "reversal_of": "5000123456" } }

{ "msg_id": "ECQ300-RT-5000123500-2026-0001", "seq": 45140, "event_type": "RETURN_POSTED", "occurred_at": "2026-10-21T12:00:00+05:30",
  "goods_movement": { "material_doc": "5000123500", "doc_year": 2026, "doc_item": 1, "movement_type": "122", "debit_credit": "H",
                      "article": "000000001101000101", "quantity": 10, "uom": "PC", "po_number": "4500098765", "po_item": 10 } }
```

| Field | Req. | SAP source |
|---|---|---|
| `material_doc`, `doc_year`, `doc_item` | **yes** | `MBLNR`, `MJAHR`, `ZEILE` |
| `movement_type` | **yes** | `BWART` (101, 102, 122, 161 …) |
| `article`, `quantity`, `uom` | **yes** | `MATNR`, `MENGE` (always positive, base unit), unit |
| `debit_credit` | no | `S` \| `H` (`SHKZG`) — the direction |
| `posting_date` | no | `BUDAT` |
| `generic_article`, `colour` | no (send them) | Generic article, colour |
| `site`, `storage_location` | no | `WERKS`, `LGORT` |
| `po_number`, `po_item` | no | `EBELN`, `EBELP` if the movement refers to a PO |
| `reversal_of` | no | Original material document (`SMBLN`) for a reversal |

### `governance_outcome` (`GOV_OUTCOME`)

What happened after a verdict (buyer accepted, exception approved / rejected …). It feeds the audit trail.

```json
{
  "msg_id": "ECQ300-OUT-D-20261021-000001-01", "seq": 45188, "event_type": "GOV_OUTCOME",
  "occurred_at": "2026-10-21T15:05:12+05:30", "sap_user": "CATHEAD03",
  "governance_outcome": {
    "decision_id": "D-20261021-000001", "po_number": "4500099001", "po_item": 10,
    "outcome": "EXCEPTION_APPROVED", "by_user": "CATHEAD03", "by_role": "CATHEAD",
    "at": "2026-10-21T15:05:12+05:30", "new_qty": null, "reason_code": "FEST", "note": "Diwali build-up for DC01"
  }
}
```

| Field | Req. | Meaning |
|---|---|---|
| `decision_id` | **yes** | From the `/decisions` answer |
| `po_number` | **yes** | PO |
| `po_item` | no | Item (null = whole PO) |
| `outcome` | **yes** | `BUYER_ACCEPTED_REDUCTION` \| `EXCEPTION_REQUESTED` \| `EXCEPTION_APPROVED` \| `EXCEPTION_REJECTED` \| `PO_RELEASED` \| `PO_WITHDRAWN` \| `NOT_CHECKED_TIMEOUT` |
| `by_user` | **yes** | SAP user |
| `by_role` | no | `BUYER` \| `CATHEAD` \| `BMHEAD` \| `SYSTEM` |
| `at` | **yes** | date-time |
| `new_qty` | no | New quantity (e.g. after accepting a reduction) |
| `reason_code` | no | Code from `GET /reason-codes` |
| `note` | no | Max 500 characters (mandatory for `OTHR` by business rule) |

### Response `200`

```json
{
  "received_at": "2026-10-22T14:24:59+05:30",
  "accepted": [
    "ECQ300-PO-4500098765-20261021101502-01",
    "ECQ300-GR-5000123456-2026-0001",
    "ECQ300-GR-5000123457-2026-0001",
    "ECQ300-RT-5000123500-2026-0001",
    "ECQ300-OUT-D-20261021-000001-01"
  ],
  "duplicates": [],
  "parked": [
    { "msg_id": "ECQ300-GR-5000123999-2026-0001", "error_code": "VALIDATION_FAILED", "message": "goods_movement.article is required" },
    { "msg_id": "ECQ300-XX-1", "error_code": "VALIDATION_FAILED",
      "message": "event_type PO_SHIPPED is not one of PO_CREATED, PO_CHANGED, PO_RELEASED, PO_RELEASE_REVOKED, PO_CLOSED, PO_DELETED, GR_POSTED, GR_REVERSED, RETURN_POSTED, RETURN_REVERSED, GOV_OUTCOME" }
  ]
}
```

| Field | Meaning |
|---|---|
| `received_at` | When the tool stored the batch |
| `accepted` | `msg_id`s stored now |
| `duplicates` | `msg_id`s already received earlier (not stored again) — sending the same batch twice gives all of them here |
| `parked` | Events that failed validation, with the reason; IT is alerted. **Still a 200 — do not resend these** |

Other answers: `400` for a malformed envelope (e.g. `{"error_code":"VALIDATION_FAILED","message":"client is required",…}`),
`401` for credentials, `413` for more than 500 events or 5 MB (split), `503` for a temporary problem (retry).

---

## 7. `POST /documents` — final document (MRN) of a decided PO

Called when the PO is **really created in SAP** (e.g. after release / goods receipt, whatever document the business
names "final", normally the MRN). It saves the document number against the `request_id` of the `/decisions` call, so the
tool can report which checked POs were actually generated — not only which ones were asked about.

### Rules

- One current document per `request_id`. Sending **the same** `document_no` again is safe → `status: UNCHANGED`.
- A **different** `document_no` for the same `request_id` → `409 DOCUMENT_CONFLICT`, unless `replace = true` (a correction:
  the old number is kept in the history) → `status: REPLACED`.
- The `request_id` must belong to a `/decisions` call that answered **200**; otherwise `404 REQUEST_NOT_FOUND`.
- Same Basic authentication as the other endpoints. Retry on timeout / 503 — resending is always safe.

### Request

```json
{
  "request_id": "WI000004711-4500099001-V1",
  "document_no": "5000123456",
  "document_type": "MRN",
  "po_number": "4500099001",
  "document_date": "2026-10-26",
  "replace": false,
  "note": ""
}
```

| Field | Type | Req. | Meaning |
|---|---|---|---|
| `request_id` | string, max 80 | **yes** | The `request_id` sent to `/decisions` (when SAP did not send one, the `request_id` the tool returned) |
| `document_no` | string, max 40 | **yes** | Final document number (MRN) |
| `document_type` | string, max 20 | no | Default `MRN` |
| `po_number` | string, max 20 | no | `EKKO-EBELN`; default: the PO number of the decision |
| `document_date` | date | no | `YYYY-MM-DD` |
| `replace` | boolean | no | `true` = correct a document already stored for this `request_id` |
| `note` | string, max 500 | no | Free text |

### Response `200` (captured)

```json
{ "request_id": "WI-REDUCE-0001", "decision_id": "D-20261026-000001", "document_no": "5000123456",
  "document_type": "MRN", "status": "STORED", "received_at": "2026-10-26T14:55:03+05:30" }
```

| Field | Meaning |
|---|---|
| `status` | `STORED` (first document) \| `UNCHANGED` (same document again) \| `REPLACED` (`replace = true`) |
| `decision_id` | The decision the document belongs to |
| `received_at` | When the tool stored it |

### Error answers (captured)

| Case | HTTP | Body |
|---|---|---|
| Another document already stored | 409 | `{"error_code":"DOCUMENT_CONFLICT","message":"request_id WI-REDUCE-0001 already has document 5000123456 - send replace = true to correct it","trace_id":"T-4834f8"}` |
| `request_id` never decided | 404 | `{"error_code":"REQUEST_NOT_FOUND","message":"No answered decision for request_id WI-NOPE - post the document with the request_id of a /decisions call that answered 200","trace_id":"T-9deafd"}` |
| `document_no` missing | 400 | `{"error_code":"VALIDATION_FAILED","message":"document_no is required","trace_id":"T-00125f"}` |

---

## 8. Status of the tool side

| Item | Status |
|---|---|
| `ping`, `reason-codes`, `decisions`, `events`, `documents` | **Available for testing** on staging |
| Verdict link (`view_url`) | Available; the portal page `/verdict/<token>` opens without sign-in |
| Decision logic | Same rules as the tool's New Request screen (Single Concept, Multi Concept, Watch List, Max Permissible) |
| Data used for decisions | Uploaded BW / SAP extracts (stock, pending PO, sales, plan, BSR) + the day's approved requests |
| Events → live open-PO / stock position | **In development**: events are received and stored now, and will feed the live position. Until then decisions use the uploaded files, so run **SHADOW** mode |
| Separate `BLOCK` verdict, B&M head limit (`bm_head_required`), final reason-code list | To be confirmed with the business |
| Multi Concept `max_qty` (REDUCE for Multi Concept items) | Rules pending from the business |

**Suggested test sequence for the SAP team:**
1. SM59 connection test (`GET /ping`).
2. `GET /reason-codes`.
3. `POST /decisions` in SHADOW mode for a few real POs (ALLOW, REDUCE, WATCH, HOLD cases), then the same call again to
   check you get the same `decision_id`.
4. Timeout / NOT_CHECKED handling (e.g. a wrong password gives 401 → must continue as today).
5. `POST /events` for a PO save, a goods receipt and a GOV_OUTCOME, then the same batch again to see `duplicates`.
6. Open a `view_url` from a decision answer in a browser.
7. `POST /documents` with the MRN of a PO that was created, then the same call again (`UNCHANGED`).

The tool's administrator sees every call — request, response, timing and trace ID — on **🔌 SAP Interface** in the
portal, so results can be checked together.

# SAP R/3 interface — `/api/sap/v1` (specification v1.4)

For the SAP ABAP / workflow / Basis teams and the tool's administrators. The binding contract is
`legacy/python-app/docs/po-governance-sap-api.yaml` (OpenAPI 3.0, v1.4) with
`PO_Governance_SAP_API_Specification_v1.4.pdf`; this page describes how the tool side implements it, what is
configurable and what is still pending.

| Endpoint | Purpose | Answers |
|---|---|---|
| `GET /api/sap/v1/ping` | SM59 connection test, SAP health job (every minute) | 200, 401, 503 |
| `GET /api/sap/v1/reason-codes` | Exception reasons for the approver dropdown | 200, 401, 503 |
| `POST /api/sap/v1/events` | 1–500 PO / goods-movement / GOV_OUTCOME events from the bgRFC queue | 200, 400, 401, 413, 503 |
| `POST /api/sap/v1/decisions` | Verdict for a PO from the release workflow step | 200, 400, 401, 409, 422, 503 |

JSON is UTF-8 with snake_case field names exactly as in the YAML; timestamps ISO 8601 with offset
(`2026-10-21T11:50:00+05:30`), dates `YYYY-MM-DD`. Every non-200 answer has the spec's `Error` body
`{"error_code": "...", "message": "...", "trace_id": "T-7f3a9c"}` and the same id in the `X-Trace-Id` header — quote it
when raising an issue. SAP must treat every non-200 `/decisions` answer (and timeouts) as **NOT_CHECKED** and continue
as today; `/events` must be retried until 200.

## 1. Connection

| Item | Value |
|---|---|
| Base URL | `https://<vm-dns-name>:8443/api/sap/v1` (test copy: the test VM, connected to SAP QA only) |
| TLS | Kestrel HTTPS endpoint with the company certificate — see *HTTPS on 8443* below |
| Authentication | HTTP Basic with a **technical user** per SAP system (`sap_ecq`, `sap_ecp`), password ≥ 24 characters |
| Network | SAP application servers → VM, TCP 8443, plus the tool's **IP allow-list** (`SapInterface:AllowedIps`) |
| SM59 | Type G destination `ZPOGOV_API` (`ZPOGOV_API_TEST` in QA), path prefix `/api/sap/v1`, Basic auth, SSL active; connection test = `GET /ping` → 200 |
| Timeouts | `/decisions`: SAP waits 3 s; the tool answers 503 `DECISION_TIMEOUT` after `SapInterface:DecisionTimeoutMs` (2500 ms). `/events`: 10 s |

### Technical users (admins)

Portal → user menu → **🔌 SAP Interface** (`/admin/sap`) → *Technical users*:

* **Add**: user name (e.g. `sap_ecp`), SAP system id (`SY-SYSID`, e.g. `ECP` — `/ping` answers it as
  `source_system_recognised`), description. Leave the password empty to generate a 32-character one; it is shown **once** —
  enter it in SM59 right away. A typed password must have at least `SapInterface:MinPasswordLength` (24) characters.
* **Reset password** (new generated password, shown once), **Disable / Enable**, **Unlock**.
* Passwords are stored as PBKDF2-SHA256 hashes (the same hasher as the portal logins) in `SAP_TECH_USERS`. After
  `SapInterface:MaxFailedLogins` (5) wrong passwords in a row the user is locked for `LockoutMinutes` (15); failed logins,
  lockouts, user changes and password resets are written to the audit trail (`sap-login-fail`, `sap-locked`,
  `sap-user-add`, `sap-user-update`, `sap-password-reset`). The last call time and IP are shown per user.

### IP allow-list

`SapInterface:AllowedIps` in `POGovApi/appsettings.json` (or `appsettings.Production.json`): single addresses or CIDR
ranges, IPv4 or IPv6 — e.g. `["10.10.20.15", "10.10.21.0/24"]`. **Empty = nobody** (every call is refused). A caller
outside the list gets **401** `IP_NOT_ALLOWED` (spec §9: "401 Wrong credentials / IP not allowed"). `X-Forwarded-For` is
used only when the TCP peer is listed in `SapInterface:TrustedProxies` (a reverse proxy / load balancer in front of the
API); otherwise it is ignored and the peer address is checked. Development allows `127.0.0.1` and `::1`.

### HTTPS on 8443

`POGovApi/appsettings.json` contains `_KestrelHttpsExample` (off). For production rename it to `Kestrel` (or put it into
`appsettings.Production.json`) and fill in the certificate:

```json
"Kestrel": {
  "Endpoints": {
    "Portal":   { "Url": "http://localhost:5080" },
    "SapHttps": { "Url": "https://0.0.0.0:8443", "SslProtocols": [ "Tls12", "Tls13" ],
                  "Certificate": { "Path": "C:\\certs\\vmm-pogov.pfx", "Password": "<pfx password>" } }
  }
},
"SapInterface": { "OnlyOnPort": 8443, "AllowedIps": [ "10.10.20.15", "10.10.20.16" ], "Environment": "PRODUCTION" }
```

With `OnlyOnPort = 8443`, `/api/sap/*` answers 404 on every other port (the portal keeps using 5080). SAP imports the
company CA chain in STRUST. With IIS instead of Kestrel, bind 8443 with the certificate on the site and list IIS / ARR in
`TrustedProxies` only if it adds `X-Forwarded-For`.

## 2. `POST /decisions`

Request (the YAML example, adapted to test data):

```json
{ "request_id": "WI000004711-4500099001-V1", "mode": "SHADOW", "source_system": "ECP", "client": "300",
  "otb_check": "PASSED", "depth_check": "PASSED", "requested_at": "2026-10-21T11:50:00+05:30",
  "purchase_order": { "po_number": "4500099001", "doc_type": "NB", "vendor": "0000201345", "purch_org": "1000",
    "purch_group": "B07", "buyer_user": "BUYER07", "division": "MENS", "currency": "INR",
    "items": [ { "po_item": 10, "article": "00000000110100010101", "generic_article": "000000001101000101",
                 "colour": "WHT", "merch_category": "1101", "season": "AW26", "order_qty": 50000, "uom": "PC",
                 "net_value": 17450000.00, "delivery_date": "2026-11-15" } ] } }
```

Answer (200):

```json
{ "decision_id": "D-20261021-000002", "request_id": "WI000004711-4500099001-V1", "mode": "SHADOW",
  "overall_verdict": "REDUCE", "verdict_code": "R", "route_to": "BUYER", "bm_head_required": false,
  "summary_text": "Item 10: reduce to 16,106 - Stock + pending + this PO cover 264 days, above the 79-day norm; max 16,106 pcs",
  "rules_version": "R-P1", "data_delayed": false,
  "data_as_of": { "bw_snapshot": "2026-10-21T14:51:14+05:30", "events_lag_seconds": 0 },
  "decided_at": "2026-10-21T14:52:06+05:30",
  "items": [ { "po_item": 10, "verdict": "REDUCE", "verdict_code": "R", "max_qty": 16106,
               "reason_code": "COVER_ABOVE_NORM", "reason_text": "Stock + pending + this PO cover 264 days, above the 79-day norm; max 16,106 pcs",
               "rule_id": "COVER-01",
               "metrics": { "plan_qty": 6303, "stock_qty": 2707, "open_po_qty": 2703, "headroom_qty": 16106, "cover_days": 264 } } ] }
```

### How an item is decided

Each item is decided with the **same Tool 1 rules the buyers use on the New Request screen** (`ITool1DecisionEngine`:
routing → Single Concept *runEngineB* / Multi Concept *runEngineA* True Repeat or BOBs / NEW article), on the current
uploaded data and today's Tool 1 ledger (approved quantities of today's requests count against the headroom; SAP
decisions are **never written into** that ledger):

| Engine input | From the SAP item |
|---|---|
| GM | `generic_article` (else `article` for a single article): exact, then without leading zeros, against the Article Master / Universe Master / F2 / BSR GMs |
| Colour | Colour Master code (case-insensitive) → that code; else Colour Master description → its code; else a colour the data holds for that GM; else the value upper-cased |
| Delivery month | `delivery_date` as MM.YYYY (a date before the current month counts as the current month) |
| Quantity | `order_qty` (whole units) |

Items of one PO with the same GM + colour + delivery month (the size variants of one generic article) are decided
**together** on their summed quantity; a REDUCE `max_qty` is split over them in proportion to their quantities (they add
up to Max Permissible).

### Verdict mapping (handover §8 item 2, owner-approved) — `DecisionEngine/Sap/SapVerdictMap.cs`

| Tool decision (engine path) | SAP verdict | reason_code | rule_id |
|---|---|---|---|
| Auto Allow — cover within T + buffer (Single Concept) | ALLOW | COVER_WITHIN_NORM | COVER-00 |
| Auto Allow — no plan for the MC-MRP-Flag | ALLOW | NO_PLAN_DATA | PLAN-00 |
| Hold, quantity above Max Permissible, Max Permissible > 0 | **REDUCE** + `max_qty` | COVER_ABOVE_NORM | COVER-01 |
| Hold — Don't Raise, Max Permissible 0 | HOLD | COVER_NO_HEADROOM | COVER-02 |
| Watch List — colour mix below the minimum | WATCH (route CATHEAD) | MIX_BELOW_MIN | MIX-01 |
| Allow — ranks Q1 / Q2 (Multi Concept) | ALLOW | PEER_Q1 / PEER_Q2 | PEER-Q1 / PEER-Q2 |
| Allow — Q3 at / near the peer average (border zone) | ALLOW | PEER_Q3 | PEER-Q3-OK |
| Watch List — Q3 beyond the border zone | WATCH | PEER_Q3 | PEER-Q3-GAP |
| Watch List — Q4 softened (at / near average) | WATCH | PEER_Q4 | PEER-Q4-WATCH |
| **Block** — Q4 beyond the border zone | **HOLD** (or BLOCK, see below) | BLOCK | BLOCK-Q4 |
| **Block** — absolute block (sell-thru < 1 %/day after ≥ 4 weeks) | **HOLD** (or BLOCK) | BLOCK | BLOCK-ABS |
| Allow — BOBs: all sibling colours Q1/Q2 | ALLOW | BOBS_SIBLINGS_OK | BOBS-01 |
| Watch List — BOBs: a sibling colour Q3/Q4 | WATCH | BOBS_SIBLING_LOW | BOBS-02 |
| Allow — too little selling history (WOF gate) | ALLOW | LOW_HISTORY | WOF-00 |
| Allow — no peer pool | ALLOW | NO_PEER_POOL | PEER-00 |
| Auto Allow — new article (not in Universe Master or BSR) | ALLOW | NEW_ARTICLE | NEW-00 |
| Cannot Validate / article unknown to the tool / no colour / no delivery date | **422** `CANNOT_DECIDE` (SAP: NOT_CHECKED) — or WATCH `CANNOT_VALIDATE` (CV-00) with `UnknownItemPolicy = WATCH` | | |

* **Block → HOLD / BLOCK**: spec v1.4 has no BLOCK value, so the tool's Block is sent as **HOLD with reason_code BLOCK**.
  When SAP is ready for a new value (a breaking change for the workflow's domain values), set
  `SapInterface:BlockVerdict = BLOCK`: those items (and the overall verdict) are then `BLOCK` / code `B`, most severe,
  routed like HOLD. Nothing else changes.
* **Unknown items**: the spec defines no item-level "not checked" verdict. Default `UnknownItemPolicy = REJECT`: when any
  item cannot be decided the whole request is answered 422 with the items named in `message` (SAP records NOT_CHECKED).
  `WATCH`: those items get WATCH / CANNOT_VALIDATE and the rest is decided. If no item can be decided the answer is 422
  either way.
* `overall_verdict` = the most severe item verdict (HOLD > WATCH > REDUCE > ALLOW; BLOCK above HOLD).
* `route_to`: ALLOW → NONE, REDUCE → BUYER, WATCH → CATHEAD, HOLD / BLOCK → CATHEAD, or BMHEAD when `bm_head_required`.
* `bm_head_required`: rule hook `SapInterface:BmHead` (pending business rule, handover §8 item 5) — off (always `false`);
  when enabled, HOLD / BLOCK (and WATCH with `IncludeWatch`) of a PO whose summed `net_value` ≥ `MinNetValue`.
* `reason_text` (≤ 132) is a short text from the engine's figures; the full engine reason is kept in the decision log.
  `summary_text` (≤ 255) lists the non-ALLOW items, most severe first.
* `metrics`: Single Concept — `plan_qty` (colour plan of the month after delivery), `stock_qty` (F1), `open_po_qty`
  (F2 pending + today's approved Tool 1 quantities), `headroom_qty` (Max Permissible), `cover_days` (STR from Day 1 of the
  month after delivery, including this PO). Multi Concept — `sell_through_pct` (BSR per-day sell-thru %). Figures the
  engine path does not produce are left out.
* `rules_version` = `SapInterface:RulesVersion` (default `R-P<planner parameters version>`); `data_as_of.bw_snapshot` =
  upload time of the stock file (F1); `events_lag_seconds` = received − occurred of the most recently received event;
  `data_delayed = true` when that lag exceeds `EventsLagLimitSeconds` (60).
* `decision_id` = `D-<yyyyMMdd>-<sequence>` (`SAP_DECISION_SEQ`, 6+ digits).

### Idempotency, mode, timing

* **Idempotent**: the same `request_id` with the same body (whitespace ignored, SHA-256 of the canonical JSON) returns the
  stored answer byte for byte — same `decision_id`. The same `request_id` with a different body → **409**
  `REQUEST_ID_CONFLICT` (generate a new request_id). Parallel identical calls get one decision.
* **Mode**: `SHADOW` / `LIVE` is echoed and logged; the tool decides the same way in both (SAP routes only in LIVE).
* **Timing**: typical server time 20–60 ms per PO (p50 ≈ 30 ms on the test data, 50 parallel POs all under 1 s); hard cap
  `DecisionTimeoutMs` (2500) → 503 `DECISION_TIMEOUT`. Every call is logged with its duration (`SAP_DECISION_LOG.Duration_Ms`,
  and the API log line `SAP decision 200 request … in 31 ms [lookup=… data=… decide=… saved=…]`).

### Other answers

| Status | error_code | When |
|---|---|---|
| 400 | `INVALID_JSON` / `VALIDATION_FAILED` | Malformed body, missing mandatory field, wrong enum, `order_qty` ≤ 0, bad date (`message` names the field, e.g. `purchase_order.items[0].order_qty must be > 0`) |
| 401 | `UNAUTHORIZED` / `USER_LOCKED` / `IP_NOT_ALLOWED` | Missing / wrong credentials, locked or disabled user, caller IP not allowed |
| 409 | `REQUEST_ID_CONFLICT` | request_id reused with a different body |
| 422 | `CANNOT_DECIDE` | Article unknown to the tool, no colour / delivery date (see unknown items) |
| 503 | `DECISION_TIMEOUT`, `DATA_NOT_LOADED`, `DATABASE_UNAVAILABLE`, `SAP_INTERFACE_DISABLED`, `STORAGE_NOT_SUPPORTED`, `INTERNAL_ERROR` | Tool temporarily unable to decide — SAP continues as NOT_CHECKED |

## 3. `POST /events`

Batch envelope `source_system`, `client` (mandatory), `queue`, `sent_at`, `events[1..500]`. Each event is validated on its
own (msg_id ≤ 80, seq, event_type of the 11 types, occurred_at with offset, the payload of its type with its mandatory
fields and enums). Answer **200 only after the batch is committed** (one transaction):

```json
{ "received_at": "2026-10-21T14:52:07+05:30",
  "accepted":   ["ECQ300-PO-4500098765-20261021101502-01", "ECQ300-GR-5000123456-2026-0001"],
  "duplicates": [],
  "parked":     [ { "msg_id": "ECQ300-GR-5000123999-2026-0001", "error_code": "VALIDATION_FAILED",
                    "message": "goods_movement.article is required" } ] }
```

* **De-duplication** by `msg_id`: a resent event is listed in `duplicates` and not stored again (its row counts the repeats).
* **Parked**: an invalid event is stored with its error, listed in `parked`, logged as a warning for IT and never blocks
  the queue. An event without msg_id is parked as `NOID-<batch>-<n>`.
* **Order**: events are stored in batch order; an accepted event whose `seq` is not above the last accepted seq of its
  queue is stored and flagged `LATE` (monitoring).
* 400 for a malformed envelope, 413 for more than `MaxEventsPerBatch` (500) events or more than `MaxBodyBytes` (5 MB).
* **Pending (handover §8 items 6, 7, 9)**: applying the events to a live open-PO / stock position (BW base + events, cut-off
  rule, GRT handling). Every event is stored with `Applied = 0`; **verdicts still use the uploaded files + today's Tool 1
  ledger** until the position tables are built. The feed lag is already measured and reported (`data_delayed`).

## 4. `GET /reason-codes`, `GET /ping`

`/reason-codes` returns the active **exception** codes (`SAP_REASON_CODES`, Code_Type EXCEPTION; seeded with the spec's
proposal NEWL, FEST, VMOQ, PRCA, REPL, OTHR "to be confirmed") with `version` = date of the last change. The planning
head maintains them on `/admin/sap` → *Reason codes* (code ≤ 4, text ≤ 60 characters). The **verdict** reason codes
(table above) are stored in the same table (Code_Type VERDICT) for reference and are fixed by the rules.

`/ping`: `{"status":"ok","server_time":…,"api_version":"1.0.0","environment":"TEST|PRODUCTION","source_system_recognised":"ECP"}`
(`SapInterface:Environment`, `ApiVersion`; the system id of the technical user).

## 5. Monitoring (`/admin/sap`, admins)

* **Overview**: settings in force, decisions today / NOT_CHECKED answers / verdict counts / shadow vs live, server time
  p50 / p95 / max, event feed (last received, last seq, lag, parked, late, not applied).
* **Decisions**: filter by date, PO / request id, verdict, mode, answer (200 vs NOT_CHECKED); open one for its items (GM /
  colour matching, engine decision and full reason, metrics) and the request / response JSON.
* **Events**: type, message id, seq, PO / material document / decision id, status (parked with reason), LATE, lag, applied,
  duplicates; open one for its payload.
* **Technical users**, **Reason codes** (above), **Test decision**: a sample request built from the uploaded data runs
  through the real decision path (request_id prefixed `TEST-`, logged with the test flag; repeating it shows idempotency).

## 6. Configuration — `SapInterface` (POGovApi/appsettings.json)

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | `false` → every call 503 `SAP_INTERFACE_DISABLED` (SAP continues as today) |
| `Environment`, `ApiVersion` | `TEST`, `1.0.0` | `/ping` |
| `AllowedIps`, `TrustedProxies` | `[]` (Dev: loopback) | IP allow-list (CIDR), proxies whose X-Forwarded-For is trusted |
| `OnlyOnPort` | `0` | serve /api/sap only on this local port (8443) |
| `RulesVersion` | `""` | `rules_version`; empty = `R-P<params version>` |
| `DecisionTimeoutMs` | `2500` | hard cap → 503 |
| `EventsLagLimitSeconds` | `60` | `data_delayed` |
| `DefaultMode` | `SHADOW` | mode of the portal's test decision |
| `BlockVerdict` | `HOLD` | `HOLD` or `BLOCK` |
| `UnknownItemPolicy` | `REJECT` | `REJECT` (422) or `WATCH` |
| `BmHead` | off | `{Enabled, MinNetValue, IncludeWatch}` |
| `MaxFailedLogins`, `LockoutMinutes`, `MinPasswordLength` | 5, 15, 24 | technical users |
| `MaxEventsPerBatch`, `MaxBodyBytes` | 500, 5 MB | 413 above |

Storage: the SAP interface needs **SQL Server** (`database/sqlserver/05_sap_interface.sql`, schema v3). With
`PoGovSettings:Storage = postgres` every SAP endpoint answers 503 "SAP interface requires SQL Server storage" and the
portal screen shows the same.

## 7. Pending / to be confirmed

| Item | Status |
|---|---|
| Live open-PO / stock position from `/events` (apply PO / GRC / GRT, cut-off, double counting, GRT re-opening pending qty) | Pending — handover §8 items 6, 7, 9; events are stored with `Applied = 0` |
| BW 04:00 feed (control file, snapshot / extraction time → `bw_snapshot`) | Pending — today `bw_snapshot` = upload time of the stock file |
| `bm_head_required` limit, final reason-code list, SAP approval levels | Open with business (§8 item 5) — rule hook / table ready |
| BLOCK as its own verdict | Waits for SAP (§8 item 2) — `BlockVerdict = BLOCK` |
| Multi Concept recommended quantity per option (REDUCE / `max_qty` for Multi Concept) | Rules pending (§8 item 3) |
| Depth-bypass resubmissions (`depth_check = BYPASSED`, `previous_decision_id`) | Accepted and logged; decided like any other request |
| Alert e-mails for parked events / NOT_CHECKED | Logged as warnings and shown on `/admin/sap`; no mail yet |

# Python v1.1.7 → .NET: gap analysis and what was changed

The .NET rebuild was ported from the Python / browser tool **v1.1.4**. The Python tool has since moved to **v1.1.6**
(4 Nov 2026: single concept calculation and screen changes) and **v1.1.7** (5 Nov 2026: SAP MRAs). This page lists
every change of those two versions, where it lives in the Python package (`VMM-PO-Governance-v1.1.7-16.35.zip`,
`dev/patches/*.py`, `mra.py`, `app/mra.js`, `SAP-MRA-INTERFACE.txt`) and what was done in .NET.

Multi concept (True Repeat, BOBs, new options) and its Recommendation are **not** changed in either version, except
the Article Master stop (item 5) and the BOBs sibling list (item 12, display only).

## v1.1.6 — single concept calculation and screens

__V116_TABLE__

## v1.1.7 — SAP MRAs (single concept)

| # | Python v1.1.7 | .NET before | .NET now |
|---|---|---|---|
| 1 | **MRA file** in Data Setup: one workbook, two sheets found by headers — item lines (DOC.NO, ARTICLE CODE, Color, Generic Article, TOTAL PO QTY, DEL. DATE, LEVEL, CREATED BY) and header report (Document Number, Purchase order, Po Date, Status). Each upload **replaces** the list; qty added up per MRA; summary of what was read, counted and set aside | MRA status file (MRA No, Qty, Status; ALLOW / WATCHLIST / … statuses), merged into a line table | Same as Python: `Application/Services/V1/Sap/MraWorkbookParser.cs` (.xlsx / .xlsb / .xls), `SAP_MRA_FILE` replaced on upload, Data Setup card `MraFilePanel.razor` with the summary. Checked with the real SAP export `Req_01_1.xlsb`: 2,326 MRAs from 11,668 item rows, all matched between the sheets |
| 2 | **Open MRAs take capacity**: levels 1, 2, 4 (and 5 reached live today, PO not unreleased) added to the released POs of their colour and delivery month; 3, 6, 7, 8, 9, X not counted; delivery date passed / multi concept / not in Article Master not counted (shown). Same numbers in the New PO request and the single concept Recommendation | Not counted anywhere | `DecisionEngine/Mra/MraCapacity.cs` (port of `mra.js` classify / index). Added to `existingPO` in the single concept engine B (New PO request **and SAP `/decisions`**) and to the single concept Recommendation; PO Assessment and multi concept unchanged, as in Python |
| 3 | New PO request card: "Open MRAs from SAP counted in this colour" (month totals + MRA Nos); incoming split into released POs + open MRAs + approved today | – | Same box on the result card; the split is in the engine result |
| 4 | **Live changes** `POST /api/mra/events`, `/api/po/events` (X-API-Key): one message or `{"messages": […]}` (max 5,000); outcomes applied / no change / duplicate / older / parked / stored; every message stored | `POST /api/sap/v1/mra-status` (lines with status) | `POST /api/sap/v1/mra-events` and `/po-events` — same message fields and outcomes, but on the existing SAP interface (technical user, HTTP Basic, IP allow-list, trace id, snake_case answer) instead of a separate API key. Rules: `DecisionEngine/Mra/MraFeed.cs` (port of `mra.py`, all `test_mra.py` cases pass) |
| 5 | **MRA Queues** tab (planner, category head, buyer — own MCs): tiles per queue, decision chips, filters, "Waiting with me", group by, side panel (decision sent by SAP, where it is, why it counts, level history), By colour, Today's changes, PO queue, Excel | – | `/mra` (`MraQueues.razor`), every role; buyers / category heads see their own Brand × MC × MRP (server side) |
| 6 | **SAP Feed** page (admin): connection status, test sender, every message, two switches — 1 "Tool requests use capacity" (ON), 2 "Live PO changes" (OFF) | – | `/admin/sap-feed` (`SapFeed.razor`). Switches stored in the params document (`swToolReqCapacity`, `swLivePO`) and kept when planners save parameters. Switch 1 OFF: New PO request check only (nothing saved, server answers 409), today's approvals stop counting, Approvals tab hidden |
| 7 | Fix: a refused save (409 / 403) no longer breaks the next click | n/a (browser-only issue) | – |

### Differences from the Python tool (on purpose)

- **Endpoint and security:** live changes come in on the SAP interface (`/api/sap/v1/mra-events`, `/po-events`,
  technical user with HTTP Basic) like `/decisions`, not on `/api/mra/events` with an `X-API-Key` header. The
  message fields, outcomes and batch format are the same as `SAP-MRA-INTERFACE.txt`.
- **Storage:** SQL Server tables (`SAP_MRA_FILE`, `SAP_MRA_FILE_UPLOADS`, `SAP_MRA_FEED`, procedure `PRO_SAP_MRA`,
  schema v6) instead of JSON documents per day. PostgreSQL storage: MRAs not available (like the rest of the SAP
  interface).
- **Morning file:** replaces the list (as Python). This undoes the "merge" behaviour of 4 Nov; the v5 MRA status
  tables and endpoints are dropped.
- **SAP decisions count MRAs:** `/decisions` uses the same single concept engine as the New PO request, so open MRAs
  lower Max Permissible there too.

## Open points (from the Python release notes)

- PO Assessment does not use MRAs (kept as v1.1.6) — confirm.
- Expired MRAs stay in SAP until someone rejects them (tile "Expired, delivery date passed").
- Queue owner labels "Commercial" and "Division Head" are fixed names (no user list for them in the SCR file).
- The day-5 late rule is not applied to MRAs.

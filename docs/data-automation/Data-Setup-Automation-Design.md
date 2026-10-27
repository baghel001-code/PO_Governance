# VMM PO Governance — Data Setup Automation: Design

**Status:** proposal for review · **Date:** 27 Oct 2026 · **Applies to:** POGovApi / Admin portal (this repository)

**Decision asked for:** replace the daily manual upload in 📂 Data Setup with an automatic import: SAP BW writes the
data files every night (BW Open Hub → CSV, 04:00), the tool loads them by itself, checks them against a control file,
and keeps the previous data if anything is wrong. Manual upload stays as the fallback. In a later phase, SAP's live PO /
goods-receipt / return events (already received on `POST /api/sap/v1/events`) keep the open-PO and stock position
current during the day.

Nothing in the decision rules changes. The engines keep reading the same datasets (F1…F4, Universe Master, BSR, masters);
only the way the datasets arrive changes.

---

## 1. Summary

| | Today | After phase 1 (BW feed) | After phase 3 (live position) |
|---|---|---|---|
| Who loads the data | A planner, by hand, every morning | Nobody — the tool, automatically at ~04:30 | Same, plus SAP events during the day |
| How | Excel / CSV / ZIP upload in Data Setup | BW Open Hub CSV files + control file in a landing folder | + `POST /events` applied to a live open-PO / stock position |
| Freshness | Whenever someone uploads (often mid-morning) | Previous day's close, ready before office hours | Near real time for POs, receipts and returns |
| Checks | Visual; row counts in the upload report | Control file: row count + total qty per file, master checks, held-aside rows | + event order / cut-off checks |
| If data is missing | Decisions run on yesterday's (or older) upload | Previous base is kept, BW team + planning are emailed, `data_delayed` can be raised | Same |
| SAP `bw_snapshot` | Upload time of F1 | BW snapshot time from the control file | Same |

Planned effort (tool side): **phase 1 about 5–6 weeks**, **phase 2 (plan + remaining masters) about 2 weeks**, **phase 3
about 6–8 weeks** after the open questions in §12 are answered. BW and SAP work is on top (§11).

---

## 2. How Data Setup works today

### 2.1 The datasets

The tool stores eleven datasets. Each upload creates a new **version** (gzip JSON in `dataset_versions`); the
**manifest** says which version is current, and the engines read an in-memory snapshot of the current versions.

| Key | Name on screen | What it is | Needed by | Upload |
|---|---|---|---|---|
| `f1` | F1 — Stock | Closing stock by Brand × MC × MRP × Flag × Colour (+ GM), company level | **Tool 1, SAP decisions**, Tool 2, Recommendation, Attribute Analysis | single |
| `f2` | F2 — Pending PO | Released open PO lines: PO no, GM, colour, delivery month, pending qty, value, vendor | **Tool 1, SAP, Tool 2** (its line list), Recommendation | single |
| `f3` | F3 — Sales Trend | Daily sales of the last 45 days by colour (`Day 1` = oldest) | **Tool 1, SAP**, Tool 2, Recommendation, Attribute Analysis | single |
| `f4` | F4 — Forecast | Monthly plan by Brand × MC × MRP × Flag (`MM.YYYY` columns) | **Tool 1, SAP**, Tool 2, Recommendation | single |
| `um` | Universe Master | GMs of the Single Concept MRP universe (routes a GM to engine B) | **Tool 1, SAP**, Tool 2, GRC | single |
| `bsr` | BSR — All Seasons | Per GM + colour: weeks on floor, average per-day sell-through % | **Tool 1, SAP** (engine A), Recommendation, GRC | **multi** (files accumulate) |
| `artmaster` | Article Master | GM → brand, division, MC, MRP, flag, season, style, fit, material, design, vendor | Resolve map (top priority), seasons, Recommendation A, Attribute Analysis | single |
| `colmaster` | Colour Master | Colour code → description | Tool 1 colour list, SAP colour matching | single |
| `attrrel` | Attr Relevance Master | Brand × MC × MRP × Flag → assessment dimension (Style / Fit / Material / Design) | Recommendation A | single |
| `catheadmap` | SCR Products / Cat Head Mapping | Brand × MC × MRP → buyer / category-head emails | **Creates logins**, Watch List approver routing, Tool 2 / GRC split | single |
| `grc` | GRC — Buying History | Goods received by Brand × GM × colour per month / week | GRC Back Testing only | own slot |

**Bold** = mandatory: Tool 1 and SAP `/decisions` refuse to decide until all six of F1, F2, F3, F4, Universe Master and
BSR are present (`Tool1Router.MissingRequired`).

### 2.2 The upload paths

| Path (portal) | API | What it does |
|---|---|---|
| A file per slot | `IngestFile` (`Key`, `Append`) | Reads one dataset; BSR appends unless `Append=false` |
| 📦 ZIP bundle | `IngestZip` | Recognises files by name (`F1_Stock…`, `UniverseMaster…`) or as raw exports; reports missing required files |
| 🧩 Raw export import | `IngestRaw` | Recognises SAP/BW report layouts by their headers and converts them (stock: `Stk q + In Q` → `OP STK`; sales: `Calendar Day` / `Sales Qty` dates → `Day 1..N`; PO: `Sum of Pending Qty` → `Pending Qty`; article master; BSR) |
| GRC slot | `IngestGrc` | Monthly (`Cal. year` row) or weekly (`WW.YYYY`) GRC layout |

All paths end in the same commit: one **upload batch** (`UPL-yyyyMMdd-nnnn`), one new version per dataset, manifest
update, upload log + audit line, cache invalidation; for `catheadmap` also the SCR login sync. History keeps every
version, and any older version or whole upload can be made current again.

**This commit is the seam for automation:** the automatic import produces the same rows and calls the same
`SaveVersionAsync`, so the engines, History, restore and audit work unchanged.

### 2.3 Problems with the manual process

- The data is only as fresh as the last person who uploaded; a missed morning means decisions on old stock and POs.
- SAP `/decisions` already answers in real time, but on files that can be a day or more old (`bw_snapshot` = upload
  time of F1, not the real stock date).
- Large files are exported, renamed and uploaded by hand; mistakes (wrong file in a slot, old export) are only noticed by
  eye.
- BSR files accumulate per upload — an old season file can stay mixed in.
- No completeness check: a half-exported file loads as long as it has headers.

---

## 3. Target architecture

```
 ┌──────────── SAP BW ───────────┐      ┌───────────── SAP ECC / S/4 ─────────────┐
 │ process chain, 04:00          │      │ PO save / GR / return BAdIs → bgRFC     │
 │  stock · open PO · sales ·    │      └───────────────┬─────────────────────────┘
 │  article · universe · colour  │                      │ POST /api/sap/v1/events (exists)
 │  weekly: BSR                  │                      ▼
 │  Open Hub → CSV + CONTROL     │         SAP_EVENTS (stored, Applied = 0 today)
 └──────────────┬────────────────┘                      │ phase 3: applied after the cut-off
                │ SFTP / shared folder                   ▼
                ▼                              LIVE_OPEN_PO · LIVE_STOCK_ADJ
   landing folder  \\tool-server\pogov-feed\inbox                │
                │                                                │
                ▼                                                │
   ┌──────────── POGovApi: DataFeedService (new) ─────────────┐  │
   │ 1 wait for VMM_CONTROL_<date>.csv                         │  │
   │ 2 check every file: present, row count, total qty, hash   │  │
   │ 3 parse CSV → canonical rows (mapping §5)                 │  │
   │ 4 master checks; unknown GM / colour rows held aside      │  │
   │ 5 commit ALL datasets as ONE upload batch (source = bw)   │◄─┘ (phase 3: position read by
   │ 6 archive files, record run, notify, warm decision cache  │      the engines next to F1/F2)
   └──────────────┬────────────────────────────────────────────┘
                  ▼
   dataset_versions + manifest (unchanged) → DatasetCache
                  → Tool 1 · Tool 2 · SAP decisions · analytics
                  ▲
   Data Setup page: feed status, "Run import now",
                    manual upload still possible (fallback / override)
```

**Why files from BW, not a live connection to BW:** this is the delivery agreed in the Developer Brief (§04) and the
least coupling: BW owns the extraction logic and the cut-off; the tool needs no BW user, no RFC library and no network
path into BW; a failed night is visible as missing files. The alternatives are in §4.

---

## 4. Delivery options considered

| Option | How | For | Against | Verdict |
|---|---|---|---|---|
| **A. BW Open Hub → CSV + control file** (SFTP or shared folder) | BW process chain ends with an Open Hub Destination per dataset, writes CSV into the landing folder, control file last | Agreed in the Developer Brief; simple; BW keeps the business logic and the cut-off; files are an audit trail; replayable | One-day-old data until phase 3 | **Recommended** |
| B. Tool pulls BW queries (OData / BW query via HTTP) | Scheduled job in the tool calls published BW queries | No files to manage | Needs a BW technical user, query performance at night, paging of millions of rows, harder to reconcile | Fallback if Open Hub is not available |
| C. Direct database read (BW on HANA calculation views via ODBC) | SQL reads | Fast | Tight coupling to BW internals; needs HANA client and DB user; security review | Not recommended |
| D. SAP RFC / BAPI from the tool | NCo / RFC calls for stock, POs | Real time | Heavy (SAP NCo licence/library), puts query load on ECC, duplicates BW logic | Not recommended — SAP events cover the real-time part |
| E. Keep manual upload, improve it | Better ZIP rules | No project | Does not solve freshness | Stays only as fallback |

---

## 5. Dataset by dataset: source, schedule and mapping

The file names and column lists below are the ones in the Developer Brief (`VMM_<DATASET>_<YYYYMMDD>.csv`). The
**right-hand column** is what the tool's code actually reads today, so the importer must produce exactly those headers
(or the engines must be taught the new names — §9.4 recommends mapping in the importer).

### 5.1 Overview

| # | Tool dataset | File | Source | Frequency | Phase |
|---|---|---|---|---|---|
| 1 | `f1` Stock | `VMM_STOCK` | BW (closing stock, company level) | daily 04:00 | 1 |
| 2 | `f2` Pending PO | `VMM_OPENPO` | BW (released vendor POs, open qty) | daily 04:00 | 1 |
| 3 | `f3` Sales Trend | `VMM_SALES` | BW (POS sales, last 45 days) | daily 04:00 | 1 |
| 4 | `um` Universe Master | `VMM_UNIVERSE` | BW | daily 04:00 | 1 |
| 5 | `artmaster` Article Master | `VMM_ARTICLE` | BW | daily 04:00 | 1 |
| 6 | `colmaster` Colour Master | `VMM_COLOUR` | SAP (via BW) | daily 04:00 | 1 |
| 7 | `bsr` BSR | `VMM_BSR` | BW | weekly (e.g. Monday 04:00) | 1 |
| 8 | `f4` Forecast / plan | `VMM_PLAN` | SAP SFCC / SFCR — manual upload until then | when changed (later hourly) | 2 |
| 9 | `attrrel` Attr Relevance | — | Planning (manual) | when changed | stays manual |
| 10 | `catheadmap` SCR mapping | — | Merchandising (manual) — or HR/merch system later | when changed | stays manual (option: phase 2) |
| 11 | `grc` GRC buying history | `VMM_GRC` (optional) | BW goods receipts | weekly | 2 (optional) |
| — | Live PO changes, GR, returns | `POST /events` | SAP R/3 | real time | 3 |

Fixed rules from the Developer Brief that every file follows: **company level** (all sites summed), **vendor POs only
(no STO)**, open PO = **released** POs only, open qty = ordered − received + returned, **no real-time sales**.

### 5.2 `VMM_STOCK` → `f1`

| BW file column | Tool column (`f1`) | Rule |
|---|---|---|
| Generic Material | `Generic Material` | leading zeros removed |
| Colour | `Color` | colour **code** (as in Colour Master) |
| Brand Type | `Brand Type` | |
| Division | `DIV` | |
| MC CD | `MC CD` | |
| MRP | `MRP` | number, no thousand separator |
| Flag | `BOGO/NON-BOGO` | `BOGO` / `NON-BOGO` exactly as in F4 / Universe |
| Stock Qty | `OP STK` | closing stock **incl. in-transit** (today's raw export adds `Stk q + In Q`) — **confirm** |
| Article, Stock Value, As-of Date | — | stored in meta / checked; As-of Date must equal the control-file snapshot date |
| *(not in brief)* MC Desc | `MC DESC` | **add** — used by Attribute Analysis |

### 5.3 `VMM_OPENPO` → `f2`

| BW file column | Tool column (`f2`) | Rule |
|---|---|---|
| PO Number | `PO Number` | |
| PO Item | — | kept in meta; Tool 2 works per PO line = PO + GM + colour + month |
| Generic Material, Colour, Brand Type, MC CD, MRP, Flag | `Generic Material`, `Color`, `Brand Type`, `MC CD`, `MRP`, `BOGO/NON-BOGO` | as F1 |
| Delivery Date (YYYY-MM-DD) | `Delivery Month` | converted to **`MM.YYYY`** (first schedule line) |
| Pending Qty | `Pending Qty` | = ordered − received + returned (BW computes) |
| Vendor | `Vendor Name` | the tool shows the **name**; brief lists only `Vendor` → **add Vendor Name** |
| *(not in brief)* PO Value | `PO Value` | **add** — Tool 2 "₹ Value at Risk" needs it |
| *(not in brief)* Division, MC Desc, Season, Art Desc | `DIV`, `MC DESC`, `Season`, `ART DESC` | **add** (resolve map fallback when a GM is not in Article Master) |
| Ordered / Received / Returned Qty, Release Date | — | meta; used for checks and phase 3 |

### 5.4 `VMM_SALES` → `f3`

The brief delivers **long format** (one row per GM × colour × day); the tool reads **wide format** (`Day 1` … `Day 45`,
oldest first). The importer pivots:

| BW file column | Tool column (`f3`) | Rule |
|---|---|---|
| Generic Material, Colour, Brand Type, MC CD, MRP, Flag | same as F1 | one output row per GM × colour |
| Date + Sales Qty | `Day 1` … `Day N` | N = `params.mixWindowDays` (45); `Day 1` = snapshot date − N + 1; missing days = 0 |

Volume note: long format is ~45 × the wide row count. Alternative to agree with BW: deliver wide (`Day 1..45`) directly.

### 5.5 `VMM_UNIVERSE` → `um`

| BW file column | Tool column | Rule |
|---|---|---|
| Generic Material | `Generic Material` | presence = Single Concept |
| MC CD, MRP, Flag | `MC CD`, `MRP`, `Flag (BOGO/NON-BOGO)` | |
| Concept (Single), Valid From | — | only rows with Concept = Single and Valid From ≤ snapshot date are loaded |
| *(not in brief)* Brand Type, Division, MC Desc | `Brand Type`, `Division`, `MC DES` | **add** (resolve map) |

### 5.6 `VMM_ARTICLE` → `artmaster`

| BW file column | Tool column | Rule |
|---|---|---|
| Generic Material | `Generic Material` | one row per GM (article/colour rows collapsed) |
| Brand Type, Division, MC CD, MC Desc, MRP, Flag, Season | `Brand Type`, `Division`, `MC CD`, `MC DESC`, `MRP`, `Flag (BOGO/NON-BOGO)`, `Season` | Season drives Current / Fallback season selection |
| Style, Fit, Product Material, Design | same names | Recommendation dimensions, Attribute Analysis |
| Vendor, Vendor Name | `Vendor`, `VENDOR NAME` | |
| *(not in brief)* Article description | `ART DESC` | **add** |

### 5.7 `VMM_COLOUR` → `colmaster`

| BW file column | Tool column | Rule |
|---|---|---|
| Colour Code | `Colour` | the tool's lookup reads `Colour`/`Color` and `Colour Desc`/`Color Desc`/`Description` — **the brief's names do not match**, the importer must rename |
| Colour Description | `Colour Desc` | |

### 5.8 `VMM_BSR` → `bsr` (weekly)

| BW file column | Tool column | Rule |
|---|---|---|
| Generic Material, Colour, MC CD, MRP, Flag | `Generic Material`, `Color`, `MC CD`, `MRP`, `Article Type` | |
| Weeks on Floor | `WEEK ON FLOOR` | |
| Avg Per-Day Sell-Thru % | `AVG PERDAY SELL THRU%` | percent number (e.g. `5.2` → stored as `5.2%`) |
| Season, Snapshot Week | — | the file holds **all seasons**; season of a GM still comes from Article Master |

Change of behaviour: today BSR **accumulates** over uploads; the feed **replaces** BSR with the weekly file (one complete
all-seasons file). Days without a new BSR file keep the current BSR version.

### 5.9 `VMM_PLAN` → `f4` (phase 2)

| File column | Tool column | Rule |
|---|---|---|
| MC CD, MRP, Flag (+ Brand Type — **add**) | `MC CD`, `MRP`, `BOGO/NON-BOGO`, `Brand Type` | one row per Brand × MC × MRP × Flag |
| Month + Plan Qty | `MM.YYYY` columns | pivot long → wide |
| Plan Version | — | load only when the version changed |

Until SFCC/SFCR is connected, the planner keeps uploading F4 by hand; the feed never overwrites a manually uploaded F4
with an older plan version.

### 5.10 What stays manual

- **Attr Relevance Master** and **SCR Products / Cat Head Mapping**: business-maintained, change rarely; keep the
  Data Setup slots. SCR upload keeps creating logins.
- **Parameters** and **Season Selection** (Data Setup sidebar).
- Manual upload of **any** dataset stays possible as an override (§8.4).

---

## 6. File contract (to agree with the BW team)

| Item | Rule |
|---|---|
| Location | `\\<tool-server>\pogov-feed\inbox` (shared folder) **or** SFTP account `pogov_feed` → same folder. Write-only for BW, read/move for the tool service account |
| File names | `VMM_<DATASET>_<YYYYMMDD>.csv`, date = **snapshot (business) date**, e.g. `VMM_STOCK_20261026.csv` |
| Encoding | UTF-8 (BOM optional), comma separator, `"` quoting, first row = header, **fixed column order** |
| Numbers | dot decimal, no thousand separators, no currency signs; empty = 0 only where stated |
| Dates | `YYYY-MM-DD`; timestamps ISO 8601 with offset |
| Codes | GM and article **without** leading zeros (or the importer strips them); colour = Colour Master code |
| Writing | write to `*.tmp`, rename to `.csv` when complete; **control file last** |
| Control file | `VMM_CONTROL_<YYYYMMDD>.csv`: `File Name, Row Count, Total Qty, Snapshot Date, Extraction Time` — one line per data file of that night (`Total Qty` = sum of the quantity column: Stock Qty / Pending Qty / Sales Qty / Plan Qty; empty for masters) |
| Cut-off | `Extraction Time` = the moment the snapshot represents (e.g. `2026-10-26T23:59:59+05:30`); becomes `bw_snapshot` in SAP answers and the cut-off for phase 3 |
| Re-delivery | BW may re-deliver a night by writing the same names again with a new control file; the tool re-imports |
| Weekly BSR | listed in the control file only on BSR days |

---

## 7. Import process (DataFeedService)

A new background service in POGovApi, following the pattern of the existing `BackupBackgroundService` (polls every few
minutes, acts once per business day after a configured hour, uses the business time zone `Asia/Kolkata`).

### 7.1 Schedule

| Setting | Default | Meaning |
|---|---|---|
| `DataFeed:Enabled` | `false` | master switch (off until go-live) |
| `DataFeed:InboxDir` / `ArchiveDir` / `ErrorDir` | `D:\pogov-feed\inbox` … | folders |
| `DataFeed:StartAt` | `04:15` | start looking for today's control file |
| `DataFeed:LateAfter` | `06:30` | no control file by then → "feed late" alert |
| `DataFeed:GiveUpAt` | `09:00` | stop waiting; decisions keep yesterday's base (alert sent) |
| `DataFeed:PollMinutes` | `5` | check interval |
| `DataFeed:MaxAgeDays` | `1` | snapshot older than this → `data_delayed` warning on SAP answers (optional) |
| `DataFeed:AlertTo` | BW team, planning, IT | email recipients |

### 7.2 Steps of one run

1. **Detect.** `VMM_CONTROL_<date>.csv` for a date newer than the last successful import appears.
2. **Completeness.** Every file named in the control file exists and is complete (no `.tmp`); unknown extra files are
   ignored and reported.
3. **Reconcile.** For each file: row count = control `Row Count`; sum of the quantity column = `Total Qty` (to the unit);
   SHA-256 recorded. Any mismatch → **run fails** (step 9).
4. **Parse & map.** Read the CSV with the existing delimited reader (UTF-8 path), map columns per §5 into the tool's
   canonical headers, convert dates (`Delivery Date` → `MM.YYYY`), pivot sales and plan to wide format, strip leading
   zeros.
5. **Masters first.** Load Colour, Article and Universe masters into the new snapshot before the transactional files.
6. **Reference checks.** F1 / F2 / F3 / BSR rows whose GM is in no master, or whose colour is not in Colour Master, are
   **held aside** (not loaded) and listed in `DATA_FEED_REJECT`; a run fails only if held-aside rows exceed a threshold
   (`DataFeed:MaxRejectPct`, default 2 %).
7. **Plausibility.** Compare with the current base: total stock, total open PO qty and row counts within ±`MaxSwingPct`
   (default 30 %) of yesterday, else **warning** (not failure) for planning to look at.
8. **Commit (one transaction per run).** One upload batch `UPL-…` with source **`bw-feed`** and the control-file date
   as label; one new version per dataset (same `SaveVersionAsync` as manual uploads); manifest updated once; BSR
   **replaced** (not appended) when a BSR file is present; F4 only when the plan version changed. `bw_snapshot` = control
   `Extraction Time` (stored in the dataset meta of F1, read by `SapDecisionService.BwSnapshot`). Cache invalidated;
   `DecisionWarmUpService` loads the new snapshot within 30 s.
9. **On failure.** Nothing is committed; the **previous base stays current**; files move to `ErrorDir/<date>/`; the
   run is recorded with the reason; alert email to `AlertTo`; the Data Setup page shows the red feed status. Manual
   upload or "Run import now" after BW re-delivers.
10. **Archive & record.** Files move to `ArchiveDir/<date>/` (kept `ArchiveKeepDays`, default 60); run, files and
    counts recorded; audit line `📂 Upload` with "BW feed".

### 7.3 Idempotency and replays

- A control-file date is imported once; re-delivery (new control file, same date) imports again and replaces that
  night's versions (new upload batch, old one stays in History).
- An older date than the current base is never imported automatically (admin can force it from the portal).
- Running two imports at once is prevented by the store lock already used for uploads.

---

## 8. Changes in the tool

### 8.1 Database (new script `06_data_feed.sql`, schema v5)

| Table | Columns (main) | Purpose |
|---|---|---|
| `DATA_FEED_RUN` | Run_Id, Snapshot_Date, Started_At, Finished_At, Status (RUNNING / OK / WARNING / FAILED / LATE), Reason, Upload_Id, Extraction_Time, Triggered_By (schedule / admin) | one row per import attempt |
| `DATA_FEED_FILE` | Run_Id, File_Name, Dataset_Key, Rows_Expected, Rows_Read, Qty_Expected, Qty_Read, Sha256, Status, Message | per file |
| `DATA_FEED_REJECT` | Run_Id, Dataset_Key, Line_No, Reason, Row_Json | held-aside rows (kept 60 days) |

Procedures `PRO_DATA_FEED_RUN` (`START` / `FILE` / `FINISH` / `LIST` / `GET`) and `PRO_DATA_FEED_REJECT`, QPS style
(`@Cmd`, `@Error_Code`, `@Error_Message`), like the SAP procedures. `upload_batches.rec.source` gets the value
`bw-feed`; nothing else in the existing tables changes.

### 8.2 Code

| Where | What |
|---|---|
| `Application/Services/V1/DataFeed/` | `DataFeedService` (steps 1–10), `FeedFileContract` (expected columns per file), `FeedMapper` (BW columns → canonical rows, pivots), `FeedValidator` |
| `Application/Interfaces/V1/DataFeed/` | `IDataFeedRepository`, `IFeedSource` (folder today; SFTP or BW-pull later behind the same interface) |
| `Infrastructure/Services/V1/DataFeedBackgroundService.cs` | schedule (as `BackupBackgroundService`) |
| `Infrastructure/Repositories/DataFeedSqlServerRepository.cs` | the procedures |
| `Infrastructure/Services/V1/Mail/` | SMTP alert sender (`Smtp` settings) — the tool has no mail sender today |
| `Application/Services/V1/Ingest/DataIngestService` | unchanged; the feed reuses `CommitAsync` / `SaveVersionAsync` |
| `SapDecisionService.BwSnapshot` | prefer `meta.extractionTime` of F1 over the upload time |
| `POGovApi/Controllers/V1/DataFeedController.cs` | `DataFeedStatus`, `DataFeedRuns`, `DataFeedRunGet`, `DataFeedRunNow`, `DataFeedRejectsExport` (staff; run-now admin) + API_MASTER rows |

### 8.3 Portal

- **📂 Data Setup**: a new top card **"Automatic data feed"** — last run (date, status, snapshot time, rows per
  dataset), next expected run, **Run import now** (admin), link to the run details and the held-aside rows (⬇ Excel).
  Each slot shows its source: "BW feed · 26 Oct snapshot" or "Manual upload by … (overrides the feed until the next
  run)".
- **🗂 History → Data uploads**: type **"BW feed"** next to ZIP bundle / Raw import / File upload; make-current works as
  today.
- Header pill: amber when today's feed is late or failed ("Data from 25 Oct").
- SAP Interface → Overview: `bw_snapshot` shown with its source (feed / upload).

### 8.4 Manual upload after go-live

Kept for: emergencies (feed failed), F4 until phase 2, Attr Relevance and SCR. A manual upload of a fed dataset
becomes current until the next successful feed run, which replaces it again (the slot says so). Optionally
(`DataFeed:LockFedSlots`) the fed slots can be hidden for planners and left to admins only.

### 8.5 Fixes to make on the way (found while preparing this document)

- Upload size limit is read from three different keys (`Ingest:MaxUploadMb`, `max_upload_mb`, `PoGovSettings:MaxUploadMb`)
  and hard-coded at 400 MB in the portal — unify on `PoGovSettings:MaxUploadMb`.
- Colour Master lookup does not accept `Colour Code` / `Colour Name` headers (the test fixture uses them) — accept them
  or document the required headers.
- ZIP and Raw imports **replace** BSR while slot uploads **append** — make the behaviour explicit in the UI.

---

## 9. Phase 3 — live open-PO and stock position from SAP events

SAP already sends PO, goods-receipt and return events (`POST /api/sap/v1/events`, stored in `SAP_EVENTS` with
`Applied = 0`). Phase 3 applies them so that decisions see POs raised, changed, received or returned **today**, and the
same-day Tool 1 ledger can be switched off (handover §8 item 9).

### 9.1 Model

```
live open PO (per PO item)   = BW open PO (f2 base, snapshot T)
                               + PO_CREATED / PO_CHANGED / PO_RELEASED after T   (full current state per PO)
                               − PO_CLOSED / PO_DELETED / RELEASE_REVOKED after T
                               − GR_POSTED  + GR_REVERSED                         (received)
                               + RETURN_POSTED − RETURN_REVERSED                  (only if a return re-opens the qty — §12 Q1)
live stock adjustment        = + GR_POSTED − GR_REVERSED − RETURN_POSTED + RETURN_REVERSED   (after T)
```

- Only events with `occurred_at` **after the BW cut-off T** (control-file extraction time) are applied — this prevents
  double counting (spec v1.4 p.4).
- Each new BW base clears the previous day's adjustments and re-applies the events of the new day after T.
- PO events carry the **full PO state**, so the newest state per PO replaces older ones (order by `seq`).

### 9.2 Changes

| Item | Change |
|---|---|
| Tables | `LIVE_OPEN_PO` (PO, item, GM, colour, delivery month, open qty, last seq, last event), `LIVE_STOCK_ADJ` (GM, colour, qty delta, last seq) |
| Applier | background service applying `ACCEPTED` events in `seq` order every few seconds, setting `Applied = 1` / `Applied_At` |
| Engines | Tool 1 B / SAP decisions read open PO = live open PO instead of F2 + ledger; stock = F1 + live adjustment. Behind a switch `LivePosition:Enabled` |
| `data_delayed` | already computed from the event lag — becomes meaningful |
| Ledger | same-day Tool 1 ledger off when live position is on (Watch List approvals in the tool stay until the SAP workflow takes them over) |

---

## 10. Testing, cut-over and rollback

1. **File contract test** — BW delivers one sample night to a test folder; the importer runs in **dry-run** mode
   (`DataFeed:DryRun = true`: validates and reports, commits nothing).
2. **Parallel run, 2–3 weeks** — feed runs on the staging server every night; planners keep uploading by hand in
   production. A daily **reconciliation report** compares feed vs manual per dataset: row counts, total qty, and the
   **Tool 2 decision mix** and a sample of Tool 1 decisions on both bases.
3. **Go-live criteria** — 10 consecutive nights with: feed complete before 06:30, control checks passing, held-aside
   rows < 0.5 %, Tool 2 decision differences explained.
4. **Go-live** — `DataFeed:Enabled = true` in production; planners stop daily uploads (slots remain).
5. **Rollback** — switch the feed off; manual upload works exactly as today; History lets admins make any previous
   upload current again.

Automated tests: mapping per file (CSV fixtures for every `VMM_*` file), pivot of sales / plan, control-file mismatch,
held-aside rows, idempotent re-delivery, BSR replace, F4 version skip, failure keeps the previous base, SQL procedures
(SQL Server test suite).

---

## 11. Plan, effort and responsibilities

| Phase | Scope | Tool team | BW / SAP team | Duration |
|---|---|---|---|---|
| 0 | Agree file contract (§6), answer §12 | 2–3 days | Open Hub design | 1–2 weeks |
| 1 | Importer for STOCK, OPENPO, SALES, UNIVERSE, ARTICLE, COLOUR, BSR; run log; alerts; Data Setup feed card; dry run | ~4 weeks build + test | Open Hub destinations, process chain, folder/SFTP | 5–6 weeks incl. parallel run |
| 2 | PLAN from SFCC/SFCR; optional GRC file; SCR from a system source (if available) | ~2 weeks | SFCC/SFCR extract | 2–3 weeks |
| 3 | Live open-PO / stock position from SAP events; ledger off | ~5–6 weeks | confirm event rules (§12), bgRFC in production | 6–8 weeks |

| Who | Responsible for |
|---|---|
| BW team | extraction logic, Open Hub, process chain timing, control file, re-delivery |
| SAP team | events (already built), GRT rule, PO workflow |
| Planning | parallel-run review, F4 until phase 2, Attr Relevance, parameters |
| Merchandising | SCR mapping |
| IT infrastructure | landing folder / SFTP, service account, SMTP relay, folder backup |
| Tool team | importer, run log, portal, alerts, tests, reconciliation report |

---

## 12. Open questions (need answers before build)

| # | Question | Owner | Why it matters |
|---|---|---|---|
| 1 | Does a goods return (122 / 161) **re-open** the pending PO quantity? (handover §8 item 6) | SAP + Planning | open-PO formula, phase 3 |
| 2 | Exact extraction list and **cut-off time**; POS posting time vs 04:00 (handover §8 item 7) | BW | `bw_snapshot`, double counting |
| 3 | Does `Stock Qty` include **in-transit**? (today's raw export adds `Stk q + In Q`) | BW + Planning | F1 = what the engines treat as stock |
| 4 | Can `VMM_OPENPO` add **PO Value, Vendor Name, Division, MC Desc, Season, Art Desc**; `VMM_STOCK` add **MC Desc**; `VMM_UNIVERSE` add **Brand Type, Division, MC Desc**; `VMM_ARTICLE` add **Art Desc**? | BW | fields the tool uses today (§5) |
| 5 | Sales file **wide** (`Day 1..45`) or long? | BW | volume, pivot effort |
| 6 | BSR day of the week; is it always all seasons in one file? | BW | BSR replace logic |
| 7 | Folder vs SFTP; server path; who monitors | IT | §6 |
| 8 | Who receives failure emails; is an SMTP relay available? | IT + Planning | alerts |
| 9 | Plan (SFCC/SFCR): version field, frequency, Brand Type in the key | Planning + SAP | phase 2 |
| 10 | Should manual upload of fed datasets stay open to planners after go-live? | Planning | §8.4 |
| 11 | Approximate row counts per file (stock, open PO, sales) | BW | sizing; tool currently tested with 400 MB uploads |

---

## Appendix A — Columns the tool reads today, per dataset (exact headers)

Header matching for most engines ignores case and spaces (`SheetRow.Col`); the Recommendation, Attribute Analysis, GRC
and Cat-Head lookups need the **exact** header — the importer therefore always writes the first name listed.

| Dataset | Columns (first name = the one the importer writes; alternates accepted today) |
|---|---|
| f1 | `Generic Material` · `Color` (Colour) · `Brand Type` · `DIV` (Division) · `MC CD` · `MC DESC` · `MRP` · `BOGO/NON-BOGO` (Flag, Article Type, Flag (BOGO/NON-BOGO)) · `OP STK` (Op Stk) |
| f2 | `PO Number` (PO No, Purchase Order …) · `Generic Material` · `Color` · `Brand Type` · `DIV` · `MC CD` · `MC DESC` · `MRP` · `BOGO/NON-BOGO` · `Delivery Month` (`MM.YYYY`; DEL MONTH) · `Pending Qty` (Released Qty) · `PO Value` (PO Val, Value, PO Amount) · `Vendor Name` (Vendor) · `Season` · `ART DESC` |
| f3 | `Generic Material` · `Color` · `Brand Type` · `MC CD` · `MRP` · `BOGO/NON-BOGO` · `Day 1` … `Day N` (`D N`; oldest first) — or `MM.YYYY` month columns / `CY APR & MAY AVG` |
| f4 | `Brand Type` · `MC CD` · `MRP` · `BOGO/NON-BOGO` · `MM.YYYY` month columns |
| um | `Generic Material` · `Brand Type` · `Division` · `MC CD` · `MC DES` · `MRP` · `Flag (BOGO/NON-BOGO)` |
| bsr | `Generic Material` · `Color` · `MC CD` · `MRP` · `Article Type` (BOGO/NON-BOGO, Flag) · `WEEK ON FLOOR` (WOF) · `AVG PERDAY SELL THRU%` |
| artmaster | `Generic Material` · `ART DESC` · `Brand Type` · `Division` · `MC CD` · `MC DESC` · `MRP` · `Flag (BOGO/NON-BOGO)` · `Season` · `Style` · `Fit` · `Product Material` · `Design` · `Vendor` · `VENDOR NAME` (any other column = an attribute in Attribute Analysis) |
| colmaster | `Colour` (Color) · `Colour Desc` (Color Desc, Description, Desc) |
| attrrel | `Brand Type` · `MC CD` · `MRP` · `BOGO/NON-BOGO` · `Assessment Dimension` |
| catheadmap | `Brand Type` · `MC CD` · `MRP` · buyer / category-head email columns (Level 1/2 → buyer, Level 3 → category head; `Category_Email_ID` for Tool 2 / GRC) |
| grc | own layout: `Brand Type`, `Generic Material`, art description, `Colour`; monthly `MM.YYYY` under a `Cal. year` row or weekly `WW.YYYY` with `GRC Q` / `GRC V` |

Formats: delivery / plan months `MM.YYYY`; day columns `Day N`; numbers as plain numbers; per-day sell-through as a
percentage.

## Appendix B — Sources

- `legacy/python-app/docs/PO_Governance_Developer_Brief_Stage1_2_3.pdf` §04 (data sources, Open Hub, file list, server
  steps), `PO_Governance_SAP_API_Specification_v1.4.pdf` (cut-off rule, SAP fields), `DEVELOPER-HANDOVER.md` §5 and §8
  (future sources, open items 6, 7, 9), `PO_Governance_Functional_Logic_Guide.pdf` p.2 (files).
- Code: `DecisionEngine/Model/DatasetKeys.cs`, `Application/Services/V1/Ingest/LegacyImport.cs`,
  `DataIngestService.cs`, `Application/Services/V1/Platform/DataService.cs`, `Infrastructure/Services/V1/DatasetCache.cs`,
  `BackupService.cs`, `database/sqlserver/05_sap_interface.sql` (`SAP_EVENTS`).

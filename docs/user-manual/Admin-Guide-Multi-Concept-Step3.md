# PO Governance — Administrator Guide: Multi Concept Rule, Step 3

**For:** PO Governance administrators · **Change:** multi concept rule Step 3 (percentile bands and the B&M Head
exception) · **Date:** 23 Nov 2026, updated 27 Nov 2026 (the exception needs the minimum; new card view)

This guide covers only what an administrator has to do for Step 3: set it up, check it works, and keep it running.
The full rule for buyers and planners is in the User Manual (section 3.4).

---

## 1. What changed

For articles **not** in the Universe Master (multi concept), the tool no longer uses quartiles (Q1–Q4). It places the
option by its **position among similar options** (same Brand, MC, MRP and Flag), on its per-day sell-through of the
first 3 weeks:

| Band | Position | Decision (New Request) | PO Assessment | SAP `/decisions` |
|---|---|---|---|---|
| **Top 20 %** | above the 80th percentile | **Allow** — if it also meets its Entry / Mid / Fashion minimum (Entry 2.0 %, Mid 1.5 %, Fashion 1.5 % per day) | Retain | ALLOW `PEER_Q1` |
| Top 20 %, below the minimum | | **Block** | Cancel | BLOCK `BLOCK-EMF-MIN` |
| Top 20 %, MC-MRP not in the mapping | | **Hold** — "Planner has not maintained this MC-MRP in the master." | Cannot Validate | HOLD `EMF_NOT_MAINTAINED` |
| **20–30 %** | above the 70th, up to the 80th percentile | **Exception — B&M Head** — only if it also meets its Entry / Mid / Fashion minimum | Exception — B&M Head | **HOLD `PEER_EXCEPTION`, `route_to` = BMHEAD, `bm_head_required` = true** |
| 20–30 %, below the minimum | | **Block** (never reaches Level 8) | Cancel | BLOCK `BLOCK-EMF-MIN` |
| 20–30 %, MC-MRP not in the mapping | | **Hold** — "Planner has not maintained this MC-MRP in the master." | Cannot Validate | HOLD `EMF_NOT_MAINTAINED` |
| **Below the top 30 %** | 70th percentile or lower | **Block** | Cancel | BLOCK `BLOCK-PEER` |

- **New colour of an existing option (BOBs):** the other colours with 4+ weeks on floor are placed the same way and the
  **worst** one decides — any below 30 % → Block; any in 20–30 % → Exception; all in the top 20 % → Allow. In both the
  Exception and the Allow case the **oldest** other colour must meet the minimum (below → Block, not mapped → Hold).
- **Unchanged:** less than 4 weeks on floor → Allow; new articles → Allow; below 1 %/day after 4+ weeks → Block (any band).
- **The exception (from 27 Nov 2026):** only an option in the 20–30 % band that **meets its Entry / Mid / Fashion
  minimum** goes to Level 8 — the buyer may **raise an exception** (MRA Level 8) by typing a
  reason, or choose **Don't raise** (nothing is saved). The exception waits for the **B&M Head** on the Approvals page.
  **Only the B&M Head and admins** can approve or reject it — category heads and planners cannot.

---

## 2. Before go-live — checklist

Do these in order, first on Staging, then on Production.

| # | Task | Where | Done |
|---|---|---|---|
| 1 | Run the latest `database/sqlserver/05_sap_interface.sql` (adds the reason code `PEER_EXCEPTION`; safe to re-run) | SQL Server Management Studio, database `POGovernance` | ☐ |
| 2 | Register Anne (B&M Head) in the portal with the **Category Head** role | 👥 Users → **+ Add user** | ☐ |
| 3 | Put Anne's login email in the API settings (`BmHeadEmail`) | API server, `appsettings` | ☐ |
| 4 | Restart the API (recycle the IIS site / restart the service) | API server | ☐ |
| 5 | Make sure the planners have uploaded the **Entry / Mid / Fashion Mapping** | 📂 Data Setup | ☐ |
| 6 | Tell the SAP team about the new HOLD for the B&M Head (section 6) | — | ☐ |
| 7 | Check it end to end (section 4) | Portal, 🔌 SAP Interface | ☐ |

### 2.1 Register the B&M Head (Anne)

1. Tab **👥 Users** → **👥 Users** → **+ Add user**.
2. Type Anne's **Employee ID** → **🔍 Fetch employee** and check the card (name, email, status Active).
3. Tick **Category Head** (enough to see the ✅ Approvals tab; Admin also works) → **Add user**.
4. Note the **email** shown for her — the setting in 2.2 must be exactly this email.

If Anne already has a login (e.g. as a buyer), use **Edit** and add the **Category Head** role.

### 2.2 Set the B&M Head in the API settings

On the API server, in the API's settings file for that environment — `appsettings.Production.json` in production (it is
not in git; create it next to `appsettings.json` if it does not exist), or `appsettings.json` — add under
`PoGovSettings`:

```json
{
  "PoGovSettings": {
    "BmHeadEmail": "<Anne's portal login email>",
    "BmHeadName": "B&M Head"
  }
}
```

| Setting | Meaning |
|---|---|
| `BmHeadEmail` | The B&M Head's portal login email (upper / lower case does not matter). **Empty = exceptions wait for an admin.** |
| `BmHeadName` | Shown as the approver while that email has no portal login; once Anne is registered her own name is shown. |

Then **restart the API** — the setting is read at start-up.

> Exceptions raised **before** the setting was filled keep their approver (an admin). Decide those yourself on the
> Approvals page; new exceptions go to Anne.

---

## 3. Approving an exception (admins can, too)

Admins see every exception and may decide it, e.g. when Anne is away.

1. Tab **✅ Approvals** → **⏳ Waiting for me**. An exception shows **Why an exception (20–30% band)** — the tool's
   reason with the percentile — and the buyer's **Reason for the exception**.
2. To approve: keep or lower **Approve quantity** (0 up to the requested quantity) → **✓ Approve**.
3. To reject: write a **Comment** (required) → **✕ Reject**.

The buyer sees the decision immediately in Requests and History. You cannot decide your own request.

---

## 4. Check that it works

**In the portal (Staging):**

1. As a buyer, run a **New Request** for a multi concept article whose position is between the 70th and 80th percentile.
   and that sells at least its Entry / Mid / Fashion minimum. The card shows **Exception — B&M Head** (orange), with
   *Step 3 — Rank in peer group (… percentile — … styles, Brand + MC + MRP + Flag)*, the dot on the yellow
   **20–30 % · Exception** part of the bar, and *Step 4 — EMF minimum* with **Result = ✓ MEETS THE MINIMUM**.
   - A 20–30 % article that sells **below** its minimum shows **Block** (Step 4: ✗ BELOW THE MINIMUM) — no exception.
2. Submit it: the dialog **"Raise an exception to the B&M Head?"** asks for a reason. Type one and **Send to the B&M
   Head**. The message reads **"Exception — sent to Anne (B&M Head) for approval (MRA Level 8)"**.
   - If it says **"… the admin (no B&M Head set)"**, the setting in 2.2 is missing or the API was not restarted.
3. Sign in as Anne: the ✅ Approvals tab shows the request with a red count. Sign in as a planner or category head: the
   exception is **not** in their list.

**SAP side (🔌 SAP Interface):**

- **🏷 Reason codes** — the read-only list of verdict codes includes **`PEER_EXCEPTION`** (after step 1 of the checklist).
- **🧪 Test decision** — run a decision for a 20–30 % article: the item is **HOLD / `PEER_EXCEPTION` / `EXC-L8`**, the
  answer has **`route_to` = `BMHEAD`** and **`bm_head_required` = true**; the verdict page (🔗 Open verdict page) shows
  **B&M head needed: yes**.
- **⚖️ Decisions** — filter verdict **HOLD** to find the exceptions SAP received.

---

## 5. Troubleshooting

| What you see | Why | What to do |
|---|---|---|
| "Exception — sent to the admin (no B&M Head set)"; exceptions wait for an admin | `BmHeadEmail` is empty, or the API was not restarted | Fill `BmHeadEmail` (2.2) and restart the API |
| Anne does not see the exceptions | Her login email differs from `BmHeadEmail`, her login is inactive, or she has no Category Head / Admin role (no Approvals tab) | Compare the email on 👥 Users with the setting; tick **Active**; add the role |
| A planner or category head gets "This exception is waiting for the B&M Head (…)" | By design — only the B&M Head and admins decide exceptions | Nothing; Anne or an admin decides it |
| Many **Hold — "Planner has not maintained this MC-MRP in the master."** | The Entry / Mid / Fashion Mapping is missing, or does not have that Brand × MC × Flag × MRP | Planners upload / complete the mapping on 📂 Data Setup. The data's `NON-BOGO` matches the mapping's `Normal`; a **blank flag** is never found |
| The mapping upload lists **rejected rows** | A tag other than Entry, Mid or Fashion, a missing Brand / MC / Flag / MRP, or one key tagged two ways | Correct those rows in the file and upload it again (each upload replaces the whole mapping) |
| Running `05_sap_interface.sql` stops with *"String or binary data would be truncated"* | An old copy of the script | Use the latest script from the release |
| SAP shows the HOLD but sends it to Commercial, not the B&M Head | SAP-side routing | SAP team: route `route_to` = `BMHEAD` / reason `PEER_EXCEPTION` to MRA **Level 8** for the B&M Head (section 6) |

---

## 6. For the SAP team (pass on)

- New item answer for the **20–30 % band**: verdict **HOLD**, `reason_code` **`PEER_EXCEPTION`**, `rule_id` **`EXC-L8`**,
  reason text *"In the 20–30% band of its peer group (… percentile, sell-thru …%/day), meets its Entry / Mid / Fashion
  minimum: exception, needs the B&M Head's approval (MRA Level 8)"*. A 20–30 % item below its minimum is BLOCK
  `BLOCK-EMF-MIN` ("In the 20–30% band but sells …"), never sent to Level 8.
- The answer then has **`bm_head_required` = true** and **`route_to` = `BMHEAD`** (also when other items are ALLOW).
- SAP should raise these at **MRA Level 8** for the **B&M Head** (today Level 8 exceptions go to Commercial).
- Codes that keep their names, with texts now in bands: `PEER_Q1` (top 20 %), `BLOCK-PEER` (below the top 30 %),
  `BLOCK-BOBS`, `BOBS_SIBLINGS_OK`. Full contract: `docs/sap/SAP-API-Reference.md` (PDF next to it).

---

## 7. Good to know

- **Old requests** keep their Q1–Q4 labels; only new decisions use bands.
- **GRC Back Testing** and the **colour recommendations** still use their own quartile measures — they are reports, not
  decisions.
- The **Border Zone %** parameter is no longer used by any decision (kept for old runs).
- Every exception, approval and rejection is in the **📜 Audit log** (actions `request`, `approval`).

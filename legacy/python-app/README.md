# VMM PO Governance — shared server for a Windows VM

This folder runs the PO Governance tool (v89 page, with logic fixes) as a website on one Windows VM:

- **Everyone signs in** with their company email. Buyers and category heads get logins automatically from the SCR products file.
- **Planner** uploads the files once. They're saved on the server.
- **Buyers and category heads** open the same URL and get that data. They don't upload anything.
- **Watch List requests** go to the category head of that MC for approval.
- **Parameters and Season Selection** set by the Planner apply to everyone.
- **Tool 1 requests** from all users are saved centrally. The same-day Max Permissible ledger counts every user's approvals, not only the ones made in one browser tab.
- **Works with no internet.** All libraries and fonts are in the folder, and the server uses only Python's standard library.

The decision engines still run in each user's browser, exactly as before. The server stores and shares data; it does not compute any decisions.

---

## 1. What you need on the VM

- Windows Server 2016+ or Windows 10/11.
- **Python 3.8 or newer.** No `pip install` is needed. Since the VM is offline, use one of these:
  - **Option A (simplest):** on any PC with internet, download the *Windows installer (64-bit)* from python.org. Copy it to the VM and install it with **"Install for all users"** ticked.
  - **Option B (no installer):** download the *Windows embeddable package (64-bit)* zip from python.org. Unzip it into a folder named `python` inside this folder, so that `python\python.exe` exists. The scripts find it automatically.
- One free TCP port. The default is **8080**.

## 2. Install

1. Copy this whole folder to the VM, e.g. `C:\VMM-PO-Governance`.
2. Optional: edit `config.json` (see section 4).
3. Open **PowerShell as administrator** in the folder and run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install-service.ps1
   ```

   This registers a background task that starts with Windows and restarts if it stops. It also opens the port in Windows Firewall (Domain and Private networks). At the end it prints the URLs users should open, e.g. `http://VM-NAME:8080/`.

To try it without installing anything, double-click `start-server.bat` and open `http://localhost:8080/` on the VM.

| Script | What it does |
|---|---|
| `install-service.ps1` | Installs or re-installs the background task and firewall rule, then checks the server responds |
| `restart-service.ps1` | Restart after editing `config.json` or replacing `app\index.html` |
| `uninstall-service.ps1` | Removes the task and firewall rule. The `data` folder is kept |
| `start-server.bat` | Runs the server in a console window, for testing |
| `test-ai.bat` | Checks the VM can reach the AI services with the keys in `config.json` |
| `reset-admin.bat` | Emergency: resets the admin account to the default password and unlocks it (no restart needed) |

### First sign-in (admin)

1. Open the URL and sign in as **kosaraju.srihari@vishalwholesale.co.in** with password **1234**.
2. You're asked to set your own password straight away: at least 8 characters, with letters and numbers.
3. Go to **Data Setup** and upload the **SCR products** file, on its own or inside the ZIP bundle. A login is created for every buyer and category head in it. Division heads are skipped for now.
4. Check **👥 Users → Import from SCR** to confirm the right columns were read. Change them there if needed.
5. Tell people the address and the starting password **1234**. Each of them must choose their own password at first sign-in.

## 3. Who can do what

| Role | Screens | Can |
|---|---|---|
| **Admin** | Planner view + 👥 Users + ✅ Approvals | Everything below, plus add people, change roles, reset passwords, unlock accounts, see the audit log and backups |
| **Planner** | Planner view + ✅ Approvals (+ Category view from the user menu) | Upload data, change parameters, run Tool 2, see everything, clear the day's requests, approve any Watch List item |
| **Category Head** | Category view + ✅ Approvals | Raise Tool 1 requests; approve or reject Watch List requests on their MCs |
| **Buyer** | Category view | Raise Tool 1 requests; see their own requests and those on their MCs |

Only the admin can make someone a Planner or an Admin (**👥 Users → + Add user**, or **Edit** on an existing person). A person can hold several roles. Buyers and category heads see only requests on their own MCs (from the SCR file) plus their own requests. Set `restrict_to_own_mcs` to `false` to let them see everything.

## 3a. Daily use

1. **Sign in** at the URL. The top right shows your name and role. Click it to **change your password**, **sign out**, or (for planners) **switch** between the Planner and Category views.
2. **Planner → Data Setup:** upload files as usual (ZIP bundle, raw import or individual slots). Each upload gets an **Upload ID** with a timestamp and is saved to the server. Uploading a new **SCR products** file also creates logins for any new buyers or category heads in it. Existing people are never removed or demoted automatically; the admin deactivates leavers.
3. **Category view → New Request:** the data is already there. Before each decision, the page pulls every user's approvals for today. If someone else approves against the same colour while you have the *Confirm Quantity* box open, your approved quantity is re-checked. If needed it is capped, and the reason says so.
4. **Watch List approvals:** a Tool 1 request that comes out as **Watch List** is sent automatically to the category head of that Brand × MC × MRP (from the SCR file). The buyer sees "⏳ Awaiting …".
   - The category head opens **✅ Approvals** (the badge shows how many are waiting) and approves, optionally a smaller quantity, or rejects with a reason.
   - Only the approved quantity counts against the day's headroom.
   - If no category head is mapped to that MC, admins and planners can approve it.
5. **Requests** tab: today's requests you're allowed to see, with a **By** column, the approval status and an **Only mine** filter. **Clear All** (planners and admins) resets the day's ledger. The requests stay in History.
6. When the Planner uploads new files, open pages show a **"New data on the server — Load now"** banner within about 30 seconds.
7. **History tab:** pick a date range (From/To, or Today / Last 7 / 30 / 90 days / This month / All).
   - **Data uploads** (planners and admins): every upload action has its own **Upload ID** (e.g. `UPL-20260924-0003`) with date and time. A ZIP bundle is one Upload ID covering all the files inside it. **Click an Upload ID** to see every file in it. You can download one file or all of them in one Excel workbook, or use **Make this upload current** to switch everyone back to exactly those files.
   - **Tool 2 runs** (planners and admins): every *Run Assessment* is saved with a **Run ID** (`RUN-…`). **Open** shows that run's dashboard, tables and Excel export exactly as they were, with the data files and parameters of that moment. **Back to current data** returns.
   - **Tool 1 requests:** every request has a **Request ID** (`REQ-…`), including ones removed with *Clear All*, shown faded. Click an ID for details. You can search, filter by decision and export to Excel.

## 3b. Admin: 👥 Users & Security

- **Users:** search and filter by role, with each person's status (*Active*, *Not signed in yet*, *🔒 Locked*, *Inactive*) and last sign-in.
  - **+ Add user** (e.g. a new Planner) and **Bulk add…**, which takes `email, name, role` lines pasted from Excel.
  - **Edit** changes someone's name or roles, or deactivates them. Deactivating signs them out immediately.
  - **Reset password** sets it back to `1234` and makes them choose a new one.
  - **Unlock** clears a lockout. Five wrong passwords lock an account for 15 minutes.
- **Import from SCR:** shows which SCR columns hold buyer and category head emails, and lets you correct them (e.g. mark a column "Skip"). **Save & create logins** applies the change.
- **Audit log:** every sign-in (including failed ones and lockouts), upload, parameter change, request, approval, user change and backup. It has a date range and a search box.
- **Backups:** a copy of the whole `data` folder is made automatically every night after 02:00 and kept 30 days, in the `backups` folder. **Back up now** makes one immediately. Copy the `backups` folder to another drive or network share regularly; a backup on the same disk won't survive a disk failure.

## 4. `config.json`

| Setting | Default | Meaning |
|---|---|---|
| `port` | 8080 | Port the site listens on. Run `install-service.ps1` again after changing it (it updates the firewall rule) |
| `admin_email` | kosaraju.srihari@… | Always has Admin + Planner. Created automatically with the default password |
| `default_password` | `1234` | Starting password for every new or reset account. Everyone must change it at first sign-in |
| `password_min_length` | 8 | New passwords also need a letter and a number, and can't be the default or the email |
| `session_idle_hours` | 12 | Signed out after this long without using the site |
| `max_failed_logins` / `lockout_minutes` | 5 / 15 | Wrong-password lockout |
| `restrict_to_own_mcs` | true | Buyers and category heads see only their own MCs' requests |
| `backup_dir` / `backup_hour` / `backup_keep_days` | `backups` / 2 / 30 | Nightly backup of the data folder |
| `cookie_secure` | false | Set to `true` once the site runs on https:// |
| `data_dir` | `data` | Where shared data is stored |
| `max_upload_mb` | 400 | Largest single upload after compression |
| `history_retention_days` | 0 | 0 keeps all history forever. A number such as 365 deletes history older than that, checked at startup. Current files are never deleted |
| `anthropic_api_key` / `anthropic_model` | *(empty)* / `claude-sonnet-5` | Turns on **both** AI features: the chat assistant and the AI Insights tab |
| `groq_api_key` / `groq_model` | *(empty)* / `openai/gpt-oss-120b` | Optional. If set, AI Insights uses Groq instead of Anthropic. The chat always uses Anthropic |
| `outbound_proxy` | *(empty)* | e.g. `http://proxy.company.local:8080`, if the VM reaches the internet through a proxy |

### Turning on the AI features

1. Get an API key at console.anthropic.com. It needs billing set up; usage is billed per request.
2. Allow the VM outbound HTTPS (port 443) to `api.anthropic.com`, plus `api.groq.com` if you use Groq. A firewall allow-list for these addresses is enough; the VM doesn't need open internet. If you use a proxy, put it in `outbound_proxy`.
3. Paste the key into `anthropic_api_key` in `config.json`.
4. Double-click **`test-ai.bat`**. It should print `OK` for both features. If it says `FAILED 401`, the key is wrong. A "could not reach" message means the firewall or proxy is blocking the address.
5. Run `restart-service.ps1`. Users see the features on their next page load.

What each feature does:
- **Chat** (round button, bottom right): answers questions about the current screen's results, e.g. "why was X blocked?". It uses today's Tool 1 requests in Category view, and the Tool 2 run in Planner view.
- **AI Insights** (Planner tab): turns a Tool 2 run into cross-cutting findings and one recommended action.

Neither feature can change a decision. Both send the relevant result rows to the AI provider, including GM, colour, quantities, MC/MRP and reasons, so check this is acceptable under your data policy. Every AI call is recorded in `data\audit.log`.

## 5. Backup, logs, updates

- **Back up the `data` folder.** It holds everything shared:
  - `datasets\<file>\<version>.json.gz`: every uploaded version of each file; `datasets\uploads.jsonl` and `datasets\batches.jsonl` are the upload log and the Upload IDs
  - `runs\*.json.gz` + `runs\index.jsonl`: every saved Tool 2 run
  - `manifest.json`: which version of each file is current
  - `params.json`: parameters and Season Selection
  - `requests\YYYY-MM-DD.jsonl`: every Tool 1 request, one line each
  - `audit.log`: one line per upload, parameter change, request and clear
- **Disk space:** the History → Data uploads tab shows the total. Each upload version is stored compressed, typically 5–10% of the Excel size. If it grows too large, set `history_retention_days`.
- **Logs:** `logs\server.log`, which rotates automatically.
- **Updating the page:** replace `app\index.html`. No restart is needed, because browsers re-check the file on every load.
- **Timezone:** "today's requests" uses each user's browser date. Keep the VM and users on IST.

## 6. Strongly recommended: HTTPS via IIS

Right now passwords travel over the office network unencrypted (plain `http://`). Your IT team can put the site behind HTTPS in about an hour, so it opens as `https://po-governance.company.local`:

1. Install IIS with the **URL Rewrite** and **Application Request Routing** modules.
2. Enable the ARR proxy.
3. Add a reverse-proxy rule to `http://localhost:8080/`.
4. Add your company certificate binding on port 443.
5. In `config.json` set `"cookie_secure": true` and `"host": "127.0.0.1"`, so users can only come in through IIS. Then restart.

## 7. Security notes

- **Accounts:** everyone signs in with their own email and password.
  - Passwords are stored only as salted PBKDF2 hashes in `data\users.json`, never as readable text.
  - Sessions use an HttpOnly cookie that expires after 12 hours idle.
  - Changing or resetting a password signs that person out everywhere else.
- **Access control is enforced by the server**, not just hidden in the page. A buyer calling the upload API directly still gets "not allowed".
- **Admin locked out?** Double-click `reset-admin.bat` on the VM.
- **Protect the folder:** API keys and the user file live on the VM. Restrict the folder's NTFS permissions to administrators.
- **Still open:** single sign-on with company Microsoft/Google accounts isn't included. It's a possible next step if IT wants it.

## 8. Troubleshooting

| Symptom | Fix |
|---|---|
| Badge says **Server unreachable** | Check the task is running (`Get-ScheduledTask 'VMM PO Governance'`) and read `logs\server.log`. Run `restart-service.ps1` |
| Other PCs can't open the URL | Check the firewall rule exists and the VM's network profile is Domain or Private. Try `http://<VM-IP>:8080/` |
| Port already in use | Change `port` in `config.json` and run `install-service.ps1` again |
| Upload fails with "larger than max_upload_mb" | Raise `max_upload_mb` and restart |
| Opened `index.html` by double-click | That works, but only in that browser and without sharing. Always use the server URL |
| Someone forgot their password | Admin → 👥 Users → **Reset password**. They sign in with `1234` and choose a new one |
| "Too many wrong attempts" | Wait 15 minutes, or admin → 👥 Users → **Unlock** |
| A buyer is missing | Check they're in the SCR file with an email in a column marked *Buyer* (👥 Users → Import from SCR), or add them manually |

---

## What changed compared with v89

**Logic fixes**

1. **Tool 1 now returns "Recommend Reduced Qty".** Before, when Max Permissible was above 0 but below the proposed qty, the result said *Hold — Don't Raise*. How It Works (and Step 5's own text) called this case *Reduce*. Hold now means zero headroom only. If the Planner confirms a quantity below the proposal, the saved decision stays *Reduced*; it is no longer relabelled as a clean *Auto Allow*.
2. **The Step 5 formula now matches the engine.** The screen computed `Stock at Window Start − proposed qty`, but that stock value was clamped at 0. After the Confirm Quantity re-run it also held the *confirmed* qty while subtracting the *original* one. Example from testing: the real figure was 78.75 × 39.4 − 860 = **2,244**, but the old display showed 79 × 39.4 − 104. Step 5 now shows the engine's own stock before this PO, and the exact effective threshold (78.75, not a rounded 79).
3. **Tool 2 "Qty at Risk" has one definition.** The KPI strip, vendor table and division table counted only the units to cut. The Brand → Division → MC → MRP tree counted the whole released qty, including Watch List lines. All four now count units to cut.
4. **Help text matches the code.**
   - The Tool 2 table described a two-gate rule ("gap ≤20%, TST ≥ min"). The code uses one Border Zone % parameter, default 10%.
   - The help said Tool 2 skips the BSR check. It doesn't: articles in neither Universe Master nor BSR are auto-retained.
   - The worked example now says it assumes D=60 for illustration, and shows your live D.
5. **The Tool 2 line filter no longer reads Vendor Name as a quantity.** When Pending Qty was blank, the filter fell back to the Vendor Name column, so a vendor like "3M India" counted as 3 units pending.
6. **Cleanups:**
   - A nested `<style>` tag was silently dropping the `.hw-wrap` CSS rule; it's fixed.
   - The broken `'#rbtn-${r.id}'` selector threw an error on every Tool 1 result; it's fixed.
   - The unused `runEngineB2` function is removed.

**Hosting changes**

- SheetJS, JSZip and fonts are bundled in `app\vendor`. The page makes no external requests.
- AI calls go through the server with the company key. Users no longer paste keys into the browser. One Anthropic key now powers both AI features. Defaults moved to current models, because Groq retired `llama-3.3-70b-versatile` for free and developer accounts in August 2026.
- Added: shared data, shared parameters, a central request log, conflict-safe headroom, History with Upload/Run/Request IDs, sign-in with roles, SCR-based user creation, Watch List approvals, user management, audit log and nightly backups.

`build.py` + `orig.html` (developer only): `build.py` rebuilds `app\index.html` from the original v89 page. Every change is listed there, and the build stops if the source doesn't match.

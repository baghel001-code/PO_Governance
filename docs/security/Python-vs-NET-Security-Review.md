# PO Governance — Security review of the Python tool and comparison with the .NET platform

**Scope.** Python tool v1.1.4 as kept in `legacy/python-app` (`server.py`, `accounts.py`, `storage.py`, `setup_db.py`,
`config.example.json`, browser app `app/`). The v1.1.7 MRA module (`mra.py`) was not available for this review. .NET
platform: branch `claude/admiring-shannon-0eqqlg` (POGovApi, Admin portal, SAP interface). File and line references let
each point be checked. Severity: **High** = can change business results or expose company data; **Medium** = needs
another weakness or insider access; **Low** = hardening.

---

## 1. Security issues in the Python tool

| # | Severity | Issue | Where | Impact |
|---|---|---|---|---|
| P1 | **High** | **The server trusts the decision computed in the browser.** All rules run in JavaScript (`app/index.html`, 11,800 lines); `POST /api/requests` stores the request object as the browser sends it — `decision`, quantities, `finalApprovedQty` — and only adds IDs. The Watch List approval is created only if the browser says `decision == "Watch List"`. | `server.py` 1205–1248 | A user with browser developer tools (or a script) can save a PO request as **Auto Allow** with any quantity and **skip the category head approval**; the approved quantity then counts against the colour for everyone. |
| P2 | **High** | **One shared default password `1234`** for every new user and every reset; the admin page even returns it (`defaultPassword`). | `accounts.py` 108–127, 190–198; `server.py` 750; `config.example.json` | Anyone who knows a colleague's email can sign in as that colleague before they do (first sign-in sets the password), and again after every reset. |
| P3 | **High** | **The main admin is seeded with `1234` and a fixed personal email** written in the code and the sample config. | `accounts.py` 111–116; `server.py` 47–48 | On a new or reset install, whoever signs in first with that email becomes **admin** (users, data, backups). `--reset-admin` puts it back to `1234`. |
| P4 | **High** | **Plain HTTP by default**, listening on all interfaces (`host 0.0.0.0`), `cookie_secure: false`. | `server.py` 45–60; `config.example.json` | Passwords, session cookies and all company data travel unencrypted on the network; anyone on the same network can read or hijack sessions. |
| P5 | **High** | **Every signed-in user can download every data file** (stock, open POs with values, 45-day sales, plan, BSR, SCR people mapping). `restrict_to_own_mcs` only filters the request list, not the data. | `server.py` 891–917 (`GET /api/data/<key>`, no role check) | A buyer can take the complete company buying, stock and sales data, for all brands and divisions. |
| P6 | Medium | **Open AI relay.** Any signed-in user can send any prompt (system text up to 400,000 characters) to Anthropic / Groq with the company's API key; the browser builds the prompt from company data. | `server.py` 1318–1340 | Company data leaves the network to an external AI service; unlimited cost on the company key; prompt content is not controlled by the server. |
| P7 | Medium | **Stored XSS through uploaded files.** `index.html` builds HTML with `innerHTML` about 90 times and never uses its escape function; values from uploaded Excel files (e.g. colour descriptions) are inserted as HTML. | `app/index.html` 3792, 3818 (and other `innerHTML` uses) | A crafted value in an uploaded master file runs script in every user's browser (acts as that user, e.g. an admin). |
| P8 | Medium | **Session tokens stored in clear** in the sessions store and copied into every backup zip, with the users file (password hashes). Backups are not encrypted. | `accounts.py` 266–267, 103–106; `storage.py` 690+ | Anyone who can read the data folder, the database or a backup can take over any open session, and attack the password hashes offline. |
| P9 | Medium | **Any user can lock any other user** (5 wrong passwords → 15 minutes), no CAPTCHA, only a 0.3 s delay. | `accounts.py` 239–262; `server.py` 709 | Denial of service against buyers or the admin on a busy day; password guessing is only slowed per account, not per source. |
| P10 | Medium | **Secrets in plain text** in `config.json`: database passwords (incl. the DB admin login), AI API keys. | `setup_db.py` 196–233; `config.example.json` | Anyone with file access to the server (or a copy of the folder) gets full database access and the AI keys. |
| P11 | Medium | **Development web server in production.** Python's `http.server` (documented by Python as not for production), no request time-outs, request bodies up to 400 MB read into memory. | `server.py` 437, 489–500, 1357–1366 | Easy to slow down or exhaust memory (slow or large requests); no hardening of a real web server. |
| P12 | Low | **Client IP taken from `X-Forwarded-For`** without a trusted proxy. | `server.py` 460–461 | Audit and lock-out entries can be given any IP address by the client. |
| P13 | Low | **Internal error text returned to the browser** (`"Server error: <exception>"`). | `server.py` 556–558 | Leaks internals (paths, SQL, driver messages) useful to an attacker. |
| P14 | Low | **Long sessions**: 12 hours idle by default, no absolute limit, several devices at once. | `server.py` 51; `accounts.py` 273–300 | A forgotten or stolen session stays usable for a long time. |
| P15 | Low | **No Content-Security-Policy**; inline scripts and `onclick` handlers everywhere. | `server.py` 509–519; `app/index.html` | Nothing limits the damage of P7. |

Good points in the Python tool, kept in .NET: PBKDF2-SHA256 (200,000 iterations) password hashes, random session tokens,
`HttpOnly` + `SameSite=Strict` cookie, an `X-VMM` header against cross-site form posts, path checks on file names,
an audit log.

---

## 2. Why the .NET platform is the better base

| Area | Python tool | .NET platform | Closes |
|---|---|---|---|
| **Where decisions are made** | In the browser; server stores what it receives | On the server: `Tool1Service` runs the decision engine for every check and confirmation (`Application/Services/V1/Tool1/Tool1Service.cs`); the browser only shows the result | P1 |
| **Sign-in** | Email + shared default password `1234` | Employee ID + **company (HRMS) password**, checked by the API; only employees registered by the admin; no default password; built-in admin local login can be switched off (`AllowAdminLocalLogin`) | P2, P3 |
| **Brute force / lock-out** | 0.3 s delay | **CAPTCHA** on every attempt, 5 wrong → 15 min lock, HRMS's own lock | P9 (largely) |
| **Sessions** | 12 h idle, several devices | **30 min idle** with warning, **12 h absolute**, **one device at a time**, signed out within a minute when roles change or the user is deactivated | P14 |
| **Architecture** | Browser downloads all data files and runs all rules | **Blazor Server portal**: pages are rendered on the server, raw data files never reach the browser; the browser never calls the API — the portal calls POGovApi with a static API token plus the user's session | P5 (for the browser), P6 |
| **Output encoding** | Hand-built HTML with `innerHTML` | Razor encodes every value by default | P7 |
| **Database** | JSON documents; SQL built with string formatting for table names | SQL Server **stored procedures with typed parameters** (Dapper); schema versioned and checked at start-up; option to keep the DB password encrypted (`EncryptedDbPassword`) | P10 (partly) |
| **Web server** | Python `http.server` | ASP.NET Core (Kestrel / IIS), production web server with time-outs, request limits, HTTPS/HSTS support, security headers | P4 (with HTTPS on), P11 |
| **SAP integration** | — | Per-system technical users (PBKDF2, ≥ 24-character passwords, shown once), HTTP Basic, **IP allow-list**, lock-out, trace ID per call, idempotent `request_id` with body hash, every call logged | new |
| **Quality** | Few end-to-end scripts | Compiled, typed code; **1,814 automated tests** (incl. Python parity, SQL Server, contract tests against the SAP spec) | — |
| **Operations** | Batch / PowerShell scripts | API master (`API_MASTER`, `API_BASE_URL`) per environment, structured logs (Serilog), version and environment shown in the portal | — |

### Remaining gaps in the .NET platform (recommended fixes)

To be fair, the review also found points to fix in the .NET code; none is reachable from a browser today, because the
API is only called by the portal:

| # | Gap | Where | Recommendation |
|---|---|---|---|
| N1 | `api/v1/RequestSave` (kept for parity with Python) still stores a request object as sent, like P1. The portal does not use it. | `POGovApi/Controllers/V1/DataControllers.cs` 196–204 | Remove the endpoint, or restrict it to admins. |
| N2 | `api/v1/DataGet` returns any data file to any signed-in session, like P5. | `DataControllers.cs` 33–35 | Restrict to planners and admins. |
| N3 | Error answers contain the exception text (`"Server error: …"`). | `Infrastructure/Middleware/ExceptionMiddleware.cs` 93; `SapInterfaceMiddleware.cs` 121 | Return a generic message with the trace ID; keep the detail in the log only. |
| N4 | No Content-Security-Policy header. | `Admin/Program.cs` 60–66 | Add a CSP that allows only the portal's own scripts and styles. |
| N5 | HTTPS depends on configuration (`PortalSettings:UseHttps`). | `Admin/Program.cs` 71–76 | Serve the portal over HTTPS with the company certificate in staging and production. |
| N6 | Verdict / MRA pages open without sign-in by design (90-day unguessable link). | `/verdict/<token>`, `/mra-verdict/<token>` | Keep links inside the company; shorten `VerdictLink:ValidDays` if needed. |

**Conclusion.** The Python tool's main risks (decisions trusted from the browser, shared default password, plain HTTP,
all data readable by every user) come from its design and cannot be fixed by configuration. The .NET platform moves the
rules and the data to the server, uses the company sign-in, and runs on a production web server; with N1–N5 fixed it
closes every High and Medium issue above.

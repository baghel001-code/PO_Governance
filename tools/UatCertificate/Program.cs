using System.Reflection;
using PDFKit;
using PdfSharp.Drawing;
using PdfSharp.Fonts;

// UAT certificate of the VMM PO Governance Platform: every function the tool offers, as a test checklist with a
// result column for the UAT team and the sign-off. Same design as the MRA "Download PDF" (company PDFKit).
var repo = FindRepo();
var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(repo, "docs", "uat", "PO-Governance-UAT-Certificate.pdf");
GlobalFontSettings.FontResolver = new Fonts(Path.Combine(repo, "Admin", "Fonts"));

var page = new PageSettings { MarginTop = 32, MarginBottom = 50 };
using var pdf = new PdfBuilder(page);
var kit = new Layout(pdf, page);
pdf.AddLetterhead(new LetterheadInfo
{
    CompanyName = "Vishal Mega Mart",
    Tagline = "PO Governance",
    LogoPath = Path.Combine(repo, "Admin", "wwwroot", "img", "logo.png"),
    DateOfInspection = "Date: " + DateTime.Now.ToString("dd MMM yyyy"),
    ReferenceNumber = "UAT-POG-2026-01",
    DocumentTitle = "User Acceptance Test (UAT) Certificate",
    FooterInfo = "VMM PO Governance Platform · UAT Certificate · UAT-POG-2026-01"
});

pdf.AddText("VMM PO Governance Platform – UAT Certificate", TextStyle.SubHeading);
pdf.AddText("Release: PO Governance (.NET) with SAP interface v1.4, MRA module (Python v1.1.7 parity), schema v8", TextStyle.Small);

pdf.AddSectionBar("1. PROJECT DETAILS");
KV("Project", "VMM PO Governance Platform – PO validation, PO assessment and SAP integration");
KV("Application", "Portal (Admin, Blazor) + API (POGovApi) + SAP interface /api/sap/v1");
KV("Release / build", "Branch claude/admiring-shannon-0eqqlg · database schema v8 (05_sap_interface.sql) · API master 04_api_master.sql");
KV("UAT environment", "Staging – portal http://192.168.0.175:8177 · API http://192.168.0.175:8178 · SQL Server");
KV("Business owner", "Planning / Merchandising");
KV("UAT period", "From ____________________   To ____________________");
KV("Prepared by", "______________________________");

pdf.AddSpacing(6);
pdf.AddSectionBar("2. SCOPE AND APPROACH");
pdf.AddText("Every function of the tool listed below was tested in the UAT environment with business data by the users of " +
            "each role (Buyer, Category Head, Planner, Admin) and by the SAP team for the SAP interface. For each test the " +
            "tester records the result (Pass / Fail) and remarks. A failed test is retested after the fix; open points are " +
            "listed in section 4.");
pdf.AddText("Result: write PASS or FAIL. Remarks: defect reference, data used or observation.", TextStyle.Small);

var n = 0;
void Module(string title, params (string Function, string Expected)[] rows)
{
    var cols = new[] { Col("#", 0.55), Col("Functionality", 2.1), Col("Expected result", 3.3), Col("Result", 0.85, TextAlign.Center), Col("Remarks", 1.2) };
    var data = rows.Select(r => (IReadOnlyList<string>)new[] { (++n).ToString("00"), r.Function, r.Expected, "", "" }).ToList();
    pdf.AddSpacing(6);
    kit.Need(24 + kit.LineHeight + kit.RowHeight(cols, data[0]));   // section bar + header + first row on the same page
    pdf.AddSectionBar(title);
    kit.Table(cols, data);
}

pdf.AddSpacing(4);
pdf.AddSectionBar("3. FUNCTIONALITY TESTED");

Module("3.1 SIGN-IN AND SECURITY",
    ("Sign in with Employee ID, company password and security code", "Registered, active employee signs in; 3-character code (not case-sensitive); new code after every attempt"),
    ("Only registered employees", "A valid company account that is not registered is refused (\"Employee ID or password is incorrect\"); e-mail is refused as sign-in ID"),
    ("Lock-out", "5 wrong passwords lock the account for 15 minutes; admin can unlock earlier"),
    ("One device at a time", "Signing in on another device signs out the first session with a message"),
    ("Idle time-out", "Warning one minute before; signed out after 30 minutes idle and after 12 hours"),
    ("Role / status change", "When admin changes roles or deactivates, the user is signed out within a minute with the reason"),
    ("Built-in admin and change password", "Admin e-mail sign-in while local login is on; password rules (8+ chars, letter and number)"));

Module("3.2 COMMON TO ALL USERS",
    ("Pages by role", "Each role sees only its tabs (section 2.2 of the user manual); buyers / category heads only their Brand x MC x MRP (SCR)"),
    ("View switch and status", "Planner / admin switch Category view <-> Planner view; status pill shows server date; \"New data on the server\" banner reloads"),
    ("Feedback", "User sends feedback (type, screen, article, rating) and sees the admin's reply"),
    ("How it works and How-to video", "Rule explanations with current parameter values; videos open"));

Module("3.3 BUYER – NEW PO REQUEST (TOOL 1)",
    ("Validate a request", "GM shows the article strip; colour list; delivery month; quantity; Validate gives a result card with request number"),
    ("Engines", "Single Concept (Colour STR), Multi Concept (Peer BSR) or New Product chosen by the masters"),
    ("Decisions", "Auto Allow / Allow, Watch List, Hold – Don't Raise, Block, Cannot Validate with reason and Max Permissible"),
    ("Confirm final quantity", "Single Concept: final quantity pre-filled with Max Permissible; Confirm or Don't Raise; counted for the day"),
    ("Step-by-step computation", "Op stock, mix %, month-by-month stock roll, STR and Max Permissible explained"),
    ("View Recommendation", "Alternative colours / attributes with Max Permissible per month; Download Excel"),
    ("Open MRAs counted", "Box \"Open MRAs from SAP counted in this colour\"; incoming = released POs + open MRAs + approved today"),
    ("Check-only mode", "With switch \"Tool requests use capacity\" OFF: no pop-up, nothing saved, \"Raise the request in SAP\""));

Module("3.4 REQUESTS, INSIGHTS AND HISTORY",
    ("Requests – today's list", "Today's requests with decision, approval status and approved qty; Only mine; updates when a category head decides"),
    ("Insights dashboard", "Totals, decision mix, drivers, risk by MC, flagged requests; filters by brand, division, MC, month, engine, date"),
    ("History – Tool 1 requests", "Date range, search, decision filter, request details, Download Excel; cleared requests shown faded"));

Module("3.5 CATEGORY HEAD – APPROVALS",
    ("Waiting for me", "Watch List requests on own MCs with reason; red count on the tab"),
    ("Approve / reject", "Approve with quantity 0..requested; reject needs a comment; buyer sees it at once; approved qty counts for the day"),
    ("Rules", "Own requests never shown; a decided request cannot be decided again; Decided tab shows the last 120 days"));

Module("3.6 PLANNER – DATA SETUP, PARAMETERS AND SEASONS",
    ("Upload ZIP bundle", "Files recognised by name or columns; report of loaded and missing files; data shared with everyone"),
    ("Raw export files and file slots", "SAP/BW exports converted; one slot per file; BSR several files; .xlsx/.xls/.xlsb/.csv up to 400 MB"),
    ("Data status and Clear", "Required / optional files shown green, red, amber; Clear removes a file for everyone (History keeps it)"),
    ("Parameters", "Each parameter saves at once (STR T, window D, mix %, WOF gate, peer min, buffers, late PO day ...)"),
    ("Season selection", "Current and fallback seasons from the Article Master"),
    ("SCR products file", "Creates buyer / category head mapping and logins"));

Module("3.7 PLANNER – PO ASSESSMENT (TOOL 2), GRC AND HISTORY",
    ("Run assessment", "Every F2 line assessed; Run ID saved; KPIs (Retain, Reduce, Cancel, Watch, qty and value at risk)"),
    ("Brand summary and detail view", "Drill down Brand > Division > MC > MRP > PO; why this action; STR steps; PO-wise allocation"),
    ("Exports", "Excel / CSV per category head or one ZIP; charts by division and month"),
    ("GRC Back Testing", "Assess as of, by week or month; filters; Export Row Detail; Article Trace"),
    ("History – runs and uploads", "Open a saved run as it was; delete a run; view upload files; Make current to go back to earlier data"),
    ("Clear All requests", "Clears today's requests for everyone, resets the day's ledger; kept in History as cleared"));

Module("3.8 MRA – SAP MERCHANDISE REQUESTS",
    ("Morning MRA file", "Two-sheet workbook (items + header) replaces yesterday's list; summary per level, counted and skipped"),
    ("Capacity rules", "Levels 1, 2, 4 (and 5 reached live today, PO not unreleased) count in Single Concept Max Permissible; others, expired, multi concept not"),
    ("Changes before the morning file", "SAP changes received before today's file wait as pending and are applied when the file is uploaded (data version)"),
    ("MRA Queues", "Tiles per queue, decision chips, filters, Waiting with me, group by, Download Excel; buyers / category heads see own MCs"),
    ("MRA pop-up", "Click Level / waiting with: tool decision (linked by the MRA number from /documents), working, month by month, where it is now, level changes"),
    ("Download PDF", "Pop-up saved as A4 PDF in the company report design"),
    ("By colour, Today's changes, PO queue", "Capacity per colour and month; each live change and its effect; PO queue with switch Live PO changes ON"));

Module("3.9 ADMINISTRATOR",
    ("Users – add / bulk add", "Fetch employee by ID (resigned / inactive / already registered refused); roles; bulk add \"ID, role\" lines"),
    ("Users – edit, link, unlock, delete", "Change roles, active, refresh details, link Employee ID, unlock, delete; last active admin protected"),
    ("Import from SCR", "Choose Buyer / Category Head / Skip per e-mail column; Save & create logins; nobody removed"),
    ("Audit log", "Sign-ins, lock-outs, user changes, uploads, requests, approvals, parameter changes, runs, backups"),
    ("Backups", "Nightly backup after 02:00 kept 30 days; Back up now"),
    ("All feedback", "Filter by status, reply to the user, average rating"));

Module("3.10 SAP INTERFACE – PORTAL PAGES",
    ("Overview", "Interface on/off, rules version, verdict link address, allowed IPs, today's decisions, verdict counts, response times, event lag"),
    ("Decisions and PO documents", "Filter decisions; open one with items, request / response JSON, MRN, verdict link; PO documents with / without MRN; Excel"),
    ("Events", "PO / GR / return events with status (parked with reason), order and lag"),
    ("Technical users", "Add (password shown once), reset password, disable / enable, unlock"),
    ("Reason codes", "Add, activate, deactivate exception reasons"),
    ("Test decision", "Sample request through the real rules, logged as TEST-"),
    ("SAP Feed", "Every MRA / PO message with outcome; test sender; switches Tool requests use capacity and Live PO changes"));

Module("3.11 SAP INTERFACE – API /api/sap/v1 (SAP TEAM)",
    ("Connection", "HTTP Basic technical user and IP allow-list; wrong credentials / IP give 401; GET /ping and GET /reason-codes answer"),
    ("POST /decisions", "Verdict per PO item (ALLOW / REDUCE / WATCH / HOLD / BLOCK for a confirmed slow seller) within 3 s; same request_id + body gives the same answer; other body gives REQUEST_ID_CONFLICT"),
    ("view_url of /decisions", "Opens the MRA verdict page without sign-in; shows the MRA once /documents has its number"),
    ("POST /events", "Batch stored once (duplicates by msg_id); invalid events parked with reason"),
    ("POST /documents", "MRA number stored against the request_id: STORED / UNCHANGED / REPLACED; unknown request_id gives 404 REQUEST_NOT_FOUND"),
    ("POST /mra-events", "New MRA and level changes applied; action_by and remark optional; duplicate = same msg_id for the same MRA; pending before the morning file"),
    ("POST /po-events", "PO release / unrelease stored; moves capacity only with switch Live PO changes ON"),
    ("GET /verdict-url", "MRA number returns the MRA verdict page link; unknown MRA gives 404 MRA_NOT_FOUND"));

Module("3.12 VERDICT PAGES (NO SIGN-IN)",
    ("MRA verdict page", "Decision, working, where the MRA is, level changes; note when the MRA is not linked yet; Download PDF"),
    ("PO verdict page", "Verdict, what happens next, decision details, items with figures; Print"),
    ("Link validity", "Wrong token shows \"not valid\"; after expiry (90 days) shows \"expired\""));

pdf.AddSpacing(6);
pdf.AddSectionBar("4. OPEN POINTS / OBSERVATIONS");
kit.Table(new[] { Col("#", 0.5), Col("Observation", 4.2), Col("Owner", 1.3), Col("Target date", 1.2) },
    Enumerable.Range(1, 4).Select(i => (IReadOnlyList<string>)new[] { i.ToString(), "", "", "" }).ToList());

pdf.AddSpacing(6);
pdf.AddSectionBar("5. CERTIFICATION");
pdf.AddText("We confirm that the functionalities listed in section 3 were tested in the UAT environment and meet the business " +
            "requirements, except for the open points recorded in section 4. The release is accepted for deployment to production.");
KV("UAT result", "ACCEPTED  /  ACCEPTED WITH OPEN POINTS  /  NOT ACCEPTED   (strike out what does not apply)");
pdf.AddSpacing(10);
kit.Signatures(Sign("Business Owner (Planning)"), Sign("Category / Merchandising"));
kit.Signatures(Sign("SAP Team"), Sign("IT / Application Owner"));

pdf.WithPageNumbers();
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllBytes(output, pdf.ToBytes());
Console.WriteLine($"{output} · {n} tests");

void KV(string key, string value) => pdf.AddWrappedKeyValue(key, value, 130);
static SignatureBlockInfo Sign(string role) => new() { HeaderTitle = role, Name = "Name: ____________________   Date: ____________" };
static TableColumn Col(string h, double w, TextAlign a = TextAlign.Left) => new() { Header = h, Weight = w, Align = a };

static string FindRepo()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Admin", "Dlls"))) d = d.Parent;
    return d?.FullName ?? Directory.GetCurrentDirectory();
}

/// <summary>PDFKit draws in "Arial": Arial from the Windows fonts folder, else the bundled Liberation Sans (Admin/Fonts).</summary>
sealed class Fonts : IFontResolver
{
    private readonly string _regular, _bold;
    public Fonts(string bundled)
    {
        var win = Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? "", "Fonts");
        (_regular, _bold) = File.Exists(Path.Combine(win, "arial.ttf")) && File.Exists(Path.Combine(win, "arialbd.ttf"))
            ? (Path.Combine(win, "arial.ttf"), Path.Combine(win, "arialbd.ttf"))
            : (Path.Combine(bundled, "LiberationSans-Regular.ttf"), Path.Combine(bundled, "LiberationSans-Bold.ttf"));
    }
    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new(isBold ? "b" : "r");
    public byte[] GetFont(string faceName) => File.ReadAllBytes(faceName == "b" ? _bold : _regular);
}

/// <summary>
/// Page layout on top of PDFKit's PdfBuilder (read through reflection, the DLL is not changed): a table is split at the
/// page end (PDFKit draws a too-tall row over the footer), and the cursor is moved below a signature block pair.
/// </summary>
sealed class Layout
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly PdfBuilder _pdf;
    private readonly PageSettings _page;
    private readonly FieldInfo _cursor;
    private readonly MethodInfo _ensure, _wrap;
    private readonly XFont _body;

    public Layout(PdfBuilder pdf, PageSettings page)
    {
        _pdf = pdf; _page = page;
        var t = typeof(PdfBuilder);
        _cursor = t.GetField("_cursorY", Any)!;
        _ensure = t.GetMethod("EnsureSpace", Any)!;
        _wrap = t.GetMethod("WrapText", Any)!;
        _body = new XFont("Arial", 10.0, XFontStyleEx.Regular);
        LineHeight = _body.Height + 10;
    }

    private double Cursor { get => (double)_cursor.GetValue(_pdf)!; set => _cursor.SetValue(_pdf, value); }
    private double PageHeight => (double)typeof(PdfBuilder).GetField("_pageHeight", Any)!.GetValue(_pdf)!;
    private double ContentWidth => (double)typeof(PdfBuilder).GetField("_contentWidth", Any)!.GetValue(_pdf)!;
    private double Limit => PageHeight - _page.MarginBottom;

    /// <summary>Height of one table text line (PDFKit: body font height + 10).</summary>
    public double LineHeight { get; }

    /// <summary>A new page when <paramref name="height"/> does not fit on this one.</summary>
    public void Need(double height) => _ensure.Invoke(_pdf, new object[] { height });

    public double RowHeight(IReadOnlyList<TableColumn> cols, IReadOnlyList<string> row)
    {
        var total = cols.Sum(c => c.Weight);
        var lines = 1;
        for (var i = 0; i < cols.Count && i < row.Count; i++)
        {
            var w = ContentWidth * (cols[i].Weight / total) - 8.0;
            lines = Math.Max(lines, ((List<string>)_wrap.Invoke(_pdf, new object[] { row[i] ?? "", _body, w })!).Count);
        }
        return lines * LineHeight;
    }

    /// <summary>PDFKit table, split where a row would not fit (the header repeats on the next page).</summary>
    public void Table(IReadOnlyList<TableColumn> cols, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var chunk = new List<IReadOnlyList<string>>();
        var start = Cursor; var used = LineHeight;
        foreach (var r in rows)
        {
            var h = RowHeight(cols, r);
            if (start + used + h > Limit)
            {
                if (chunk.Count > 0) _pdf.AddTable(cols, chunk);
                Need(PageHeight);                                  // next page
                chunk = new(); start = Cursor; used = LineHeight;
            }
            chunk.Add(r); used += h;
        }
        if (chunk.Count > 0) _pdf.AddTable(cols, chunk);
    }

    /// <summary>A pair of PDFKit signature blocks, the cursor moved below them.</summary>
    public void Signatures(SignatureBlockInfo left, SignatureBlockInfo right)
    {
        const double height = 120;                                 // header + name line + 50 pt signature slot + gap
        Need(height);
        var y = Cursor;
        _pdf.AddSignatureBlocks(left, right);
        Cursor = y + height;
    }
}

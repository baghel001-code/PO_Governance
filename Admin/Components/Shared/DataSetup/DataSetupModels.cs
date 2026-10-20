using System.Globalization;
using DecisionEngine.Common;
using DecisionEngine.Model;
using Domain.Entities.Store;

namespace Admin.Components.Shared.DataSetup
{
    /// <summary>Display state of one Data Setup upload slot (index.html .uslot / .us-st).</summary>
    public sealed class SlotState
    {
        public string Status { get; set; } = "";
        /// <summary>"", "ok" or "er" — the us-st class of v1.1.4.</summary>
        public string StatusClass { get; set; } = "";
        /// <summary>Tooltip of the status (the stored label, shared.js markSlot st.title).</summary>
        public string? Title { get; set; }
        public bool Loaded { get; set; }
        public bool Error { get; set; }
        public bool Busy { get; set; }
        /// <summary>0..100 while the browser streams the file; null when unknown / not uploading.</summary>
        public int? Progress { get; set; }
        /// <summary>Bumped after every upload so the &lt;input type=file&gt; is recreated (the same file can be picked again).</summary>
        public int InputKey { get; set; }

        public void Reset(string text)
        {
            Status = text; StatusClass = ""; Title = null; Loaded = false; Error = false; Busy = false; Progress = null;
        }

        public void Ok(string text, string? title = null)
        {
            Status = text; StatusClass = "ok"; Title = title; Loaded = true; Error = false;
        }

        public void Fail(string text)
        {
            Status = text; StatusClass = "er"; Title = null; Loaded = false; Error = true;
        }
    }

    /// <summary>One individual file slot of the v1.1.4 Data Setup sidebar.</summary>
    public sealed record SlotDef(string Key, string Name, string Description, bool Multiple = false, string Idle = "Click to upload", bool Clearable = false);

    /// <summary>Texts and helpers shared by the Data Setup components (index.html / shared.js).</summary>
    public static class DataSetupText
    {
        public const string Accept = ".xlsx,.xls,.xlsb,.csv";
        /// <summary>v1.1.4 config.json max_upload_mb default.</summary>
        public const long MaxUploadBytes = 400L * 1024 * 1024;
        public const int MaxFiles = 200;

        public static readonly string[] Optional = { DatasetKeys.ArtMaster, DatasetKeys.ColMaster, DatasetKeys.AttrRel, DatasetKeys.CatHeadMap };

        public static string Label(string key) => DatasetKeys.Labels.TryGetValue(key, out var l) ? l : key;

        /// <summary>shared.js fmtTime: toLocaleString('en-IN', {day:'2-digit', month:'short', hour:'2-digit', minute:'2-digit'}).</summary>
        public static string FmtTime(string? iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            if (!DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)) return iso;
            var local = d.ToLocalTime();
            return local.ToString("dd MMM, hh:mm ", CultureInfo.InvariantCulture) + (local.Hour < 12 ? "am" : "pm");
        }

        /// <summary>shared.js markSlot: "✓ 1,234 rows · UPL-… · who, 14 Oct, 03:25 pm".</summary>
        public static string SavedStatus(DatasetEntry e) =>
            "✓ " + Js.GroupIN(e.Rows) + " rows · " + (string.IsNullOrEmpty(e.UploadId) ? "on server · " : e.UploadId + " · ") +
            (string.IsNullOrEmpty(e.UpdatedBy) ? "?" : e.UpdatedBy) + ", " + FmtTime(e.UpdatedAt);

        public static string FmtBytes(long b) =>
            b < 1024 ? b + " B" : b < 1024 * 1024 ? (b / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB"
            : (b / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

        public static readonly SlotDef[] SingleConcept =
        {
            new("f1", "F1 — Stock", "Sheet: STOCK · KEY-3, OP STK, plus Generic Material (joins to Article Master for Multi Concept MRP attribute)"),
            new("f2", "F2 — Pending PO", "Sheet: F3_Released_PO · PO Number, Generic Material, Color, Delivery Month, Pending Qty, <strong>PO Value</strong> (optional, ₹ total for the line)"),
            new("f3", "F3 — Sales Trend", "Sheet: SALES TREND · KEY-2, KEY-3, COLOUR, Day 1...Day 45 cols, plus Generic Material (joins to Article Master for Multi Concept MRP attribute)"),
            new("f4", "F4 — Forecast", "Sheet: FORCAST · KEY-2, month cols"),
            new("um", "Universe Master", "Sheet: Universe_Master · Generic Material, Brand, Division, MC CD, MC DES, MRP, Flag"),
        };

        public static readonly SlotDef Bsr = new("bsr", "BSR — All Seasons (multi-file OK)",
            "DETAIL sheet · Generic Material, Color, MC CD, MRP, WOF, AVG PERDAY SELL THRU% · Uploads to this slot <strong>accumulate</strong> — dump every season's Best Seller files here, any time, in any order. Which season each row counts as Current/Fallback is decided below by Season Selection, not by upload — nothing here needs re-uploading when a new season starts.",
            Multiple: true, Idle: "Click to upload (Ctrl for multiple)", Clearable: true);

        public static readonly SlotDef[] Masters =
        {
            new("artmaster", "Article Master", "Generic Material → Brand/Division/MC/MRP/Flag, and Style/Fit/Product Material/Design (required for Multi Concept MRP)"),
            new("attrrel", "Attr Relevance Master", "Brand×MC×MRP×Flag → Assessment Dim"),
            new("colmaster", "Colour Master", "Colour code → Description"),
            new("catheadmap", "SCR Products / Cat Head Mapping", "Brand×MC CD×MRP → Buyer &amp; Category Head emails. Powers per-person Watchlist links, routes Watch List approvals, and <strong>creates logins</strong> for new buyers and category heads (Users tab)."),
        };

        public static IEnumerable<SlotDef> AllFileSlots => SingleConcept.Append(Bsr).Concat(Masters);
    }

    /// <summary>Read-only stream wrapper that reports how many bytes have been read (upload progress).</summary>
    public sealed class ProgressStream : Stream
    {
        private readonly Stream _inner;
        private readonly Action<long> _onRead;
        private long _read;

        public ProgressStream(Stream inner, Action<long> onRead)
        {
            _inner = inner;
            _onRead = onRead;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => Count(_inner.Read(buffer, offset, count));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            Count(await _inner.ReadAsync(buffer.AsMemory(offset, count), ct));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            Count(await _inner.ReadAsync(buffer, ct));

        private int Count(int n)
        {
            _read += n;
            _onRead(n);
            return n;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

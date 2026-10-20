using System.Text.Json.Serialization;
using DecisionEngine.Model;
using Domain.Entities.Store;

namespace Application.DTOs.Ingest
{
    /// <summary>One uploaded file (name as picked by the planner + its bytes).</summary>
    public sealed record IngestUpload(string FileName, byte[] Content);

    /// <summary>Upload source recorded on the upload batch (shared.js pendingSource.source).</summary>
    public static class IngestSources
    {
        public const string Files = "files";
        public const string Zip = "zip";
        public const string Raw = "raw";
        public const string Grc = "grc";
    }

    /// <summary>
    /// Result of parsing one upload action — what the page put into <c>S.files</c> and showed the planner.
    /// <see cref="Keys"/> are the datasets to save (in the order the page committed them).
    /// </summary>
    public sealed class IngestResult
    {
        public string Source { get; set; } = IngestSources.Files;
        public List<string> SourceFiles { get; set; } = new();
        public List<IngestKeyPayload> Keys { get; set; } = new();
        public IngestReport Report { get; set; } = new();
        /// <summary>A hard failure (nothing usable): the user-facing message (toast / status text without the ✗).</summary>
        public string? Error { get; set; }
    }

    /// <summary>One dataset produced by an upload: the payload to store and the label (the slot status text, shared.js X-Label).</summary>
    public sealed class IngestKeyPayload
    {
        public string Key { get; set; } = "";
        /// <summary>Label stored with the version — the slot's status text, like v1.1.4 (max 200 chars).</summary>
        public string Label { get; set; } = "";
        public int Rows { get; set; }
        /// <summary>Rows that came from this upload (differs from <see cref="Rows"/> when BSR files accumulate).</summary>
        public int NewRows { get; set; }
        [JsonIgnore] public DatasetPayload Payload { get; set; } = new();
    }

    /// <summary>
    /// What the Data Setup page showed after an upload: slot status texts, toasts, alerts and the
    /// mapping / import report (as structured entries and as the plain-text lines the page rendered).
    /// </summary>
    public sealed class IngestReport
    {
        /// <summary>Status text of the slot that was used (e.g. "✓ 1234 rows — F1.xlsx", "✗ Error reading file(s)").</summary>
        public string? Status { get; set; }
        /// <summary>True when the status was shown as an error / warning (page class "us-st er").</summary>
        public bool StatusIsError { get; set; }
        /// <summary>Status text set on each data slot, per dataset key.</summary>
        public Dictionary<string, string> SlotStatus { get; set; } = new();
        public List<string> Toasts { get; set; } = new();
        public List<string> Alerts { get; set; } = new();
        /// <summary>Notes added by the server port where v1.1.4 behaviour could not be reproduced exactly.</summary>
        public List<string> Warnings { get; set; } = new();
        /// <summary>The report box text, one line per entry (HTML of v1.1.4 without tags).</summary>
        public List<string> Lines { get; set; } = new();

        public List<IngestFileRows> Files { get; set; } = new();
        public List<IngestMatchedFile> Matched { get; set; } = new();
        public List<IngestRawDetection> Detected { get; set; } = new();
        public List<IngestSkippedSheet> Skipped { get; set; } = new();
        public List<string> Unmatched { get; set; } = new();
        public List<string> MissingRequired { get; set; } = new();
        public List<string> Errors { get; set; } = new();

        /// <summary>GRC only: "week" or "month".</summary>
        public string? GrcPeriodType { get; set; }
        /// <summary>GRC only: the periods found (MM.YYYY / WW.YYYY), oldest first.</summary>
        public List<string>? Periods { get; set; }
        /// <summary>GRC only: the period the cut-over picker pre-selects (the latest).</summary>
        public string? DefaultPeriod { get; set; }
    }

    /// <summary>Rows read from one file of a slot upload.</summary>
    public sealed record IngestFileRows(string File, int Rows, bool Ok, string? Error = null);

    /// <summary>Zip entry matched by its file name ("✓ name → label (n rows)").</summary>
    public sealed record IngestMatchedFile(string Name, string Key, string Label, int Rows);

    /// <summary>Sheet auto-converted from a raw system export ("✓ file → sheet → label (n rows)").</summary>
    public sealed record IngestRawDetection(string File, string Sheet, string Type, string Key, int Rows);

    /// <summary>Sheet the raw importer skipped and why.</summary>
    public sealed record IngestSkippedSheet(string File, string Sheet, string Reason);

    /// <summary>API response of the ingest endpoints.</summary>
    public sealed class IngestResponse
    {
        /// <summary>Upload ID of the batch (UPL-yyyyMMdd-0001); null when nothing was saved.</summary>
        public string? UploadId { get; set; }
        public string Source { get; set; } = "";
        public List<string> SourceFiles { get; set; } = new();
        public IngestReport Report { get; set; } = new();
        public List<IngestSavedKey> Saved { get; set; } = new();
        /// <summary>Manifest dataVersion after the last save.</summary>
        public long? DataVersion { get; set; }
    }

    /// <summary>One saved dataset version.</summary>
    public sealed class IngestSavedKey
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public int Rows { get; set; }
        public long DataVersion { get; set; }
        public DatasetEntry? Entry { get; set; }
        /// <summary>catheadmap only: logins created / updated from the SCR products file.</summary>
        public object? UsersSync { get; set; }
        public string? UsersError { get; set; }
    }
}

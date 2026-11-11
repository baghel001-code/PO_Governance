using System.Text.Json;
using DecisionEngine.Model;
using DecisionEngine.Mra;

namespace DecisionEngine.Results.Mra
{
    /// <summary>
    /// v1.1.7 admin switches, stored in the params document (keys swToolReqCapacity / swLivePO, as the Python tool);
    /// only the SAP Feed page (admin) changes them — a parameter save keeps the stored values.
    /// </summary>
    public sealed class MraSwitches
    {
        /// <summary>Switch 1 "Tool requests use capacity" (starts ON). OFF: New PO request is check only, nothing saved,
        /// today's tool approvals stop counting, Approvals hidden.</summary>
        public bool ToolReqCapacity { get; set; } = true;
        /// <summary>Switch 2 "Live PO changes" (starts OFF). ON: live PO messages move capacity; PO queue shown.</summary>
        public bool LivePo { get; set; }

        public const string ToolReqKey = "swToolReqCapacity";
        public const string LivePoKey = "swLivePO";

        public static MraSwitches From(EngineParams? p)
        {
            var s = new MraSwitches();
            if (p?.Extra == null) return s;
            if (p.Extra.TryGetValue(ToolReqKey, out var t)) s.ToolReqCapacity = t.ValueKind != JsonValueKind.False;
            if (p.Extra.TryGetValue(LivePoKey, out var l)) s.LivePo = l.ValueKind == JsonValueKind.True;
            return s;
        }
    }

    /// <summary>Data Setup → MRA file: what was read (nothing changes when <see cref="Ok"/> is false).</summary>
    public sealed class MraUploadResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string FileName { get; set; } = "";
        public string? UploadId { get; set; }
        public string? ItemSheet { get; set; }
        public string? HeaderSheet { get; set; }
        public int Mras { get; set; }
        public SortedDictionary<string, int> ByLevel { get; set; } = new(StringComparer.Ordinal);
        public int RowsItem { get; set; }
        public int RowsHeader { get; set; }
        public int Skipped { get; set; }
        public int Mixed { get; set; }
        public int HeaderOnly { get; set; }
        public int ItemOnly { get; set; }
        public int LevelDiff { get; set; }
        /// <summary>MRAs left out by the tool's checks (level / article / colour / date / qty not readable).</summary>
        public int Invalid { get; set; }
        public int Decisions { get; set; }
        public int DecisionsKept { get; set; }
        /// <summary>Counted against capacity right after the upload (levels 1, 2, 4, delivery date not passed, single concept, in Article Master).</summary>
        public int CountedMras { get; set; }
        public long CountedQty { get; set; }
        /// <summary>v7: the data version this file starts (= UploadId).</summary>
        public string? DataVersion { get; set; }
        /// <summary>v7: SAP changes received before the file (pending) and now applied on top of it.</summary>
        public int MovedFromPending { get; set; }
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>
    /// v7 (SAP_MRA_CONTROL): IsDailyDataUpload = today's MRA file is uploaded; until then SAP changes wait in the pending
    /// table. DataVersion = Upload_Id of the file in use (the messages applied on it carry the same version).
    /// </summary>
    public sealed class MraControl
    {
        public DateTime? DataDay { get; set; }
        public bool IsDailyDataUpload { get; set; }
        public string? DataVersion { get; set; }
    }

    /// <summary>The last morning file.</summary>
    public sealed class MraSnapshotInfo
    {
        public string UploadId { get; set; } = "";
        public string UploadDay { get; set; } = "";
        public DateTimeOffset UploadedAt { get; set; }
        public string? By { get; set; }
        public string? FileName { get; set; }
        public int MraCount { get; set; }
        /// <summary>v7: SAP changes received before this file and moved onto it by the upload.</summary>
        public int? MovedFromPending { get; set; }
        public MraUploadResponse? Counts { get; set; }
    }

    /// <summary>
    /// v8: the tool's own SAP /decisions check of the MRA (SAP does not send it back): linked only by the MRA number SAP
    /// stored as document_no against the decision with POST /documents.
    /// </summary>
    public sealed class MraToolDecision
    {
        /// <summary>SAP_DECISION_ITEMS.Id (its working is read on demand).</summary>
        public long ItemId { get; set; }
        public string DecisionId { get; set; } = "";
        public string? RequestId { get; set; }
        public string? PoNumber { get; set; }
        public DateTimeOffset DecidedAt { get; set; }
        public string? Mode { get; set; }
        /// <summary>The tool's decision as given to the buyer (Auto Allow, Recommend Reduced Qty, Watch List, Hold …).</summary>
        public string Decision { get; set; } = "";
        /// <summary>The SAP verdict of the item (ALLOW / REDUCE / WATCH / HOLD / BLOCK).</summary>
        public string? Verdict { get; set; }
        public string? Reason { get; set; }
        public double? MaxPermissible { get; set; }
        public double OrderQty { get; set; }
        /// <summary>Verdict page (the link SAP got in view_url); null when the decision had no link.</summary>
        public string? ViewUrl { get; set; }
        public bool HasWorking { get; set; }
        /// <summary>The final document (MRN = MRA number) SAP stored against the decision with POST /documents.</summary>
        public string? DocumentNo { get; set; }
        /// <summary>How it was linked: "MRA number (/documents)".</summary>
        public string MatchedBy { get; set; } = "";

        // key of the decided item (for the link)
        public string Gm { get; set; } = "";
        public string Colour { get; set; } = "";
        public string DeliveryMonth { get; set; } = "";
    }

    /// <summary>The side panel of one MRA: its decision and the working saved with it.</summary>
    public sealed class MraDecisionDetail
    {
        public string MraNo { get; set; } = "";
        public MraToolDecision? Decision { get; set; }
        public System.Text.Json.Nodes.JsonObject? Working { get; set; }
    }

    public sealed class MraNoReq
    {
        public string MraNo { get; set; } = "";
    }

    /// <summary>The MRA verdict page (no sign-in): the token of the decision's verdict link and the MRA number.</summary>
    public sealed class MraVerdictViewReq
    {
        public string Token { get; set; } = "";
        public string MraNo { get; set; } = "";
    }

    /// <summary>The MRA verdict page: the same content as the MRA Queues side panel.</summary>
    public sealed class MraVerdictView
    {
        public MraRowDto Row { get; set; } = new();
        public MraDecisionDetail Detail { get; set; } = new();
        /// <summary>Names of the MRA's Division Head / Category Head only (e-mail → name).</summary>
        public Dictionary<string, string> Owners { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    /// <summary>One MRA on the dashboard: its replayed state and where it is / whether it counts.</summary>
    public sealed class MraRowDto
    {
        public string No { get; set; } = "";
        public MraState M { get; set; } = new();
        public MraClass C { get; set; } = new();
        /// <summary>Decision group (Allow, Reduce, Watch List, Hold, Block, No Plan, Other) of the tool's linked decision; "Not sent" (shown "Not found") when none.</summary>
        public string DecGroup { get; set; } = "Not sent";
        /// <summary>v8: the tool's own decision for this MRA (null when no saved check matches).</summary>
        public MraToolDecision? ToolDecision { get; set; }
    }

    public sealed class MraPoRowDto
    {
        public string Po { get; set; } = "";
        public PoState State { get; set; } = new();
        /// <summary>Capacity effect (only with switch 2 ON).</summary>
        public PoEffect? Effect { get; set; }
    }

    /// <summary>One feed message for the SAP Feed page / Today's changes.</summary>
    public sealed class MraLogRowDto
    {
        public long Seq { get; set; }
        public DateTimeOffset At { get; set; }
        public string? By { get; set; }
        public string Kind { get; set; } = "mra";
        public MraMessage? Mra { get; set; }
        public PoMessage? Po { get; set; }
        /// <summary>The message as received (admins and planners only).</summary>
        public string? Raw { get; set; }
        public string? Error { get; set; }
        public string? Outcome { get; set; }
        public string? Reason { get; set; }
        public string? EffectText { get; set; }
        public string? EffectCls { get; set; }
    }

    /// <summary>GET state of the MRA Queues / SAP Feed pages (server.py api_GET_mra state).</summary>
    public sealed class MraStateResponse
    {
        public bool Available { get; set; } = true;
        public string? UnavailableReason { get; set; }
        public string Today { get; set; } = "";
        public MraSnapshotInfo? Snapshot { get; set; }
        /// <summary>v7: IsDailyDataUpload and the data version.</summary>
        public MraControl Control { get; set; } = new();
        /// <summary>v7: SAP changes waiting for today's MRA file (not applied yet), oldest first.</summary>
        public List<MraLogRowDto> Pending { get; set; } = new();
        public List<MraRowDto> Rows { get; set; } = new();
        public List<MraPoRowDto> Pos { get; set; } = new();
        /// <summary>Last 1,500 messages (newest last).</summary>
        public List<MraLogRowDto> Log { get; set; } = new();
        public MraSwitches Switches { get; set; } = new();
        public bool Restricted { get; set; }
        /// <summary>True when SAP can send live changes (SAP interface on, at least one active technical user).</summary>
        public bool ApiEnabled { get; set; }
        /// <summary>Queue owner names: e-mail → name.</summary>
        public Dictionary<string, string> Owners { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string Stamp { get; set; } = "";
    }

    public sealed class MraSwitchesReq
    {
        public bool? ToolReqCapacity { get; set; }
        public bool? LivePo { get; set; }
    }

    public sealed class MraSwitchesResponse
    {
        public MraSwitches Switches { get; set; } = new();
        public long ParamsVersion { get; set; }
    }

    /// <summary>SAP Feed page test sender: one message or {"messages":[…]} as JSON text.</summary>
    public sealed class MraTestSendReq
    {
        /// <summary>"mra" or "po".</summary>
        public string Kind { get; set; } = "mra";
        public string Json { get; set; } = "";
    }

    public sealed class MraFeedAnswer
    {
        public long Seq { get; set; }
        public string? Outcome { get; set; }
        public string? Reason { get; set; }
        public string? Mra { get; set; }
        public string? Po { get; set; }
    }

    public sealed class MraFeedResponse
    {
        public List<MraFeedAnswer> Results { get; set; } = new();
    }
}

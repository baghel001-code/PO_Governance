using System.Text.Json.Nodes;
using Domain.Entities.Base;

namespace Domain.Entities.Request
{
    // Request bodies of the POGovApi actions (QPS Domain/Entities/Request). What used to be route or query-string
    // parameters (ids, dates, filters, version …) is a property here. Free-form values the services read with
    // Python semantics (v1.1.4 server.py: str / int / truthiness of any JSON value) stay JsonNode, and
    // ToBody() rebuilds the JSON object the service expects (only the properties that were sent).

    /// <summary>No input.</summary>
    public sealed class EmptyReq : BaseRequest
    {
        public static readonly EmptyReq Instance = new();
    }

    /// <summary>GetSpecificApi (QPS SAP_API_DATA_Req).</summary>
    public sealed class ApiTaskReq : BaseRequest
    {
        public string? TaskName { get; set; }
        public string? Param { get; set; } = string.Empty;
    }

    /// <summary>Request-date range (yyyy-MM-dd; empty = open). HistoryUploads, HistoryRequests, RunsList.</summary>
    public class DateRangeReq : BaseRequest
    {
        public string? From { get; set; }
        public string? To { get; set; }
    }

    /// <summary>AuditQuery: range + search text.</summary>
    public sealed class AuditQueryReq : DateRangeReq
    {
        public string? Q { get; set; }
    }

    /// <summary>HistoryExportRequests: range + the screen's search text and decision filter.</summary>
    public sealed class RequestExportReq : DateRangeReq
    {
        /// <summary>Search text (Request ID, GM, colour, user, MC desc, brand, division).</summary>
        public string? Q { get; set; }
        /// <summary>Exact decision, or empty for all.</summary>
        public string? Decision { get; set; }
    }

    /// <summary>A data file (dataset key f1, f2 … bsr, grc) and optionally a version / upload. DataGet, DataClear, DataRestore, HistoryExportData.</summary>
    public sealed class DataKeyReq : BaseRequest
    {
        public string? Key { get; set; }
        public string? Version { get; set; }
        public string? UploadId { get; set; }
    }

    /// <summary>One upload batch (UPL-…). UploadRestore, HistoryExportUpload.</summary>
    public sealed class UploadIdReq : BaseRequest
    {
        public string? UploadId { get; set; }
    }

    /// <summary>A saved Tool 2 run id. RunGet, RunDelete, Tool2GetRun, Tool2Options (optional).</summary>
    public sealed class RunIdReq : BaseRequest
    {
        public string? RunId { get; set; }
    }

    /// <summary>A request day (yyyy-MM-dd; empty = today). RequestsGet, RequestsClear.</summary>
    public sealed class RequestsDayReq : BaseRequest
    {
        public string? Date { get; set; }
    }

    /// <summary>RequestSave: {request:{…}, seenSeq, force} (409 when someone else saved on the same colour since seenSeq).</summary>
    public sealed class RequestSaveReq : BaseRequest
    {
        public JsonObject? Request { get; set; }
        public JsonNode? SeenSeq { get; set; }
        public JsonNode? Force { get; set; }

        public JsonObject ToBody() => Body.Of(("request", Request), ("seenSeq", SeenSeq), ("force", Force));
    }

    /// <summary>ParamsSave: the whole planner-parameters document.</summary>
    public sealed class ParamsSaveReq : BaseRequest
    {
        public JsonObject? Params { get; set; }
    }

    /// <summary>ApprovalsGet: decided items of the last Days request days (default 90; a number or a numeric string).</summary>
    public sealed class ApprovalsGetReq : BaseRequest
    {
        public JsonNode? Days { get; set; }
    }

    /// <summary>ApprovalDecide: decision approved | rejected, qty, comment.</summary>
    public sealed class ApprovalDecideReq : BaseRequest
    {
        public string? ReqId { get; set; }
        public JsonNode? Decision { get; set; }
        public JsonNode? Qty { get; set; }
        public JsonNode? Comment { get; set; }

        public JsonObject ToBody() => Body.Of(("decision", Decision), ("qty", Qty), ("comment", Comment));
    }

    /// <summary>One user by e-mail. UserResetPassword, UserDelete.</summary>
    public sealed class UserEmailReq : BaseRequest
    {
        public string? Email { get; set; }
    }

    /// <summary>UserUpdate: only the properties sent are changed (name, roles, active, unlock).</summary>
    public sealed class UserUpdateReq : BaseRequest
    {
        public string? Email { get; set; }
        public JsonNode? Name { get; set; }
        public JsonNode? Roles { get; set; }
        public JsonNode? Active { get; set; }
        public JsonNode? Unlock { get; set; }

        public JsonObject ToBody() => Body.Of(("name", Name), ("roles", Roles), ("active", Active), ("unlock", Unlock));
    }

    /// <summary>FeedbackList: admins see all items unless Mine is set; everyone else sees their own.</summary>
    public sealed class FeedbackListReq : BaseRequest
    {
        public bool Mine { get; set; }
    }

    /// <summary>FeedbackCreate.</summary>
    public sealed class FeedbackCreateReq : BaseRequest
    {
        public JsonNode? Kind { get; set; }
        public JsonNode? Screen { get; set; }
        public JsonNode? Article { get; set; }
        public JsonNode? Message { get; set; }
        public JsonNode? Rating { get; set; }
        public JsonNode? View { get; set; }

        public JsonObject ToBody() => Body.Of(("kind", Kind), ("screen", Screen), ("article", Article), ("message", Message),
                                              ("rating", Rating), ("view", View));
    }

    /// <summary>FeedbackSetStatus (admin): status new | in progress | closed, note.</summary>
    public sealed class FeedbackSetStatusReq : BaseRequest
    {
        public string? Id { get; set; }
        public JsonNode? Status { get; set; }
        public JsonNode? Note { get; set; }

        public JsonObject ToBody() => Body.Of(("status", Status), ("note", Note));
    }

    /// <summary>AiChat (raw provider proxy, Anthropic shape).</summary>
    public sealed class AiChatReq : BaseRequest
    {
        public JsonNode? Max_Tokens { get; set; }
        public JsonNode? System { get; set; }
        public JsonNode? Messages { get; set; }

        public JsonObject ToBody() => Body.Of(("max_tokens", Max_Tokens), ("system", System), ("messages", Messages));
    }

    /// <summary>AiInsights (raw provider proxy).</summary>
    public sealed class AiPromptReq : BaseRequest
    {
        public JsonNode? Prompt { get; set; }

        public JsonObject ToBody() => Body.Of(("prompt", Prompt));
    }

    /// <summary>AssistantChatInfo: scope tool1 / tool2.</summary>
    public sealed class ScopeReq : BaseRequest
    {
        public string? Scope { get; set; }
    }

    /// <summary>Tool1Options: the GM typed in the form.</summary>
    public sealed class GmReq : BaseRequest
    {
        public string? Gm { get; set; }
    }

    /// <summary>Tool1Today: "Only mine".</summary>
    public sealed class MineReq : BaseRequest
    {
        public bool Mine { get; set; }
    }

    /// <summary>GrcTrace: one GM, optional "Assess as of" period.</summary>
    public sealed class GrcTraceReq : BaseRequest
    {
        public string? Gm { get; set; }
        public string? AsOfPeriod { get; set; }
    }

    /// <summary>AttrAnalysisLevel2 / Level3: a Brand × MC (mcKey = "MC CD|||MC Desc" from level 1) and, for level 3, the MRP × Flag bucket.</summary>
    public sealed class AttrBucketReq : BaseRequest
    {
        public string? Brand { get; set; }
        public string? McKey { get; set; }
        public string? Mrp { get; set; }
        public string? Flag { get; set; }
    }

    internal static class Body
    {
        public static JsonObject Of(params (string Name, JsonNode? Value)[] props)
        {
            var o = new JsonObject();
            foreach (var (n, v) in props)
                if (v != null) o[n] = v.DeepClone();
            return o;
        }
    }
}

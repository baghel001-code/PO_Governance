using System.Reflection;

namespace Domain.Entities.Common
{
    /// <summary>
    /// Task names of every POGovApi action (QPS AllApiNames). The portal calls
    /// <c>CallingAPI&lt;RepT, ReqT&gt;(AllApiNames.X, request)</c>; the name selects the API_MASTER row
    /// (URL, method, timeout, token). Each property defaults to its own name and can be overridden in
    /// <c>ApiNamesSettings</c> (Admin/appsettings.json) through <see cref="Initialize"/>.
    /// To add an API: action on a controller + API_MASTER row + a property here and in <see cref="ApiNames"/>
    /// + the ApiNamesSettings entry (see README "API conventions").
    /// </summary>
    public static class AllApiNames
    {
        /// <summary>The API master list itself (GetAllApi): served from MasterApiSettings, not from API_MASTER.</summary>
        public static string MasterApi { get; set; } = "All";

        /// <summary>API master: every active API_MASTER row (portal start-up) — api/v1/GetAllApi (GET).</summary>
        public static string PO_GET_ALL_API { get; set; } = nameof(PO_GET_ALL_API);
        /// <summary>API master: one row by task name — api/v1/GetSpecificApi (POST).</summary>
        public static string PO_GET_SPECIFIC_API { get; set; } = nameof(PO_GET_SPECIFIC_API);
        /// <summary>Health check (no tokens needed) — api/v1/Health (GET).</summary>
        public static string PO_HEALTH { get; set; } = nameof(PO_HEALTH);
        /// <summary>Sign in: returns the session token — api/v1/AuthLogin (POST).</summary>
        public static string PO_AUTH_LOGIN { get; set; } = nameof(PO_AUTH_LOGIN);
        /// <summary>Sign out: ends the session — api/v1/AuthLogout (POST).</summary>
        public static string PO_AUTH_LOGOUT { get; set; } = nameof(PO_AUTH_LOGOUT);
        /// <summary>Change own password (signs out other sessions) — api/v1/AuthChangePassword (POST).</summary>
        public static string PO_AUTH_CHANGE_PASSWORD { get; set; } = nameof(PO_AUTH_CHANGE_PASSWORD);
        /// <summary>The signed-in user + password rules (session check) — api/v1/AuthMe (POST).</summary>
        public static string PO_AUTH_ME { get; set; } = nameof(PO_AUTH_ME);
        /// <summary>Versions, datasets, params, pending approvals, AI flags — api/v1/GetStatus (POST).</summary>
        public static string PO_GET_STATUS { get; set; } = nameof(PO_GET_STATUS);
        /// <summary>Stored payload of a data file version — api/v1/DataGet (POST).</summary>
        public static string PO_DATA_GET { get; set; } = nameof(PO_DATA_GET);
        /// <summary>Store a new data file version (multipart, v1.1.4 rows JSON) — api/v1/DataPut (POST).</summary>
        public static string PO_DATA_PUT { get; set; } = nameof(PO_DATA_PUT);
        /// <summary>Clear the current version of a data file — api/v1/DataClear (POST).</summary>
        public static string PO_DATA_CLEAR { get; set; } = nameof(PO_DATA_CLEAR);
        /// <summary>Make an older data file version current — api/v1/DataRestore (POST).</summary>
        public static string PO_DATA_RESTORE { get; set; } = nameof(PO_DATA_RESTORE);
        /// <summary>Start an upload batch — api/v1/UploadStart (POST).</summary>
        public static string PO_UPLOAD_START { get; set; } = nameof(PO_UPLOAD_START);
        /// <summary>Make every file of an upload current again — api/v1/UploadRestore (POST).</summary>
        public static string PO_UPLOAD_RESTORE { get; set; } = nameof(PO_UPLOAD_RESTORE);
        /// <summary>Upload history — api/v1/HistoryUploads (POST).</summary>
        public static string PO_HISTORY_UPLOADS { get; set; } = nameof(PO_HISTORY_UPLOADS);
        /// <summary>Tool 1 request history — api/v1/HistoryRequests (POST).</summary>
        public static string PO_HISTORY_REQUESTS { get; set; } = nameof(PO_HISTORY_REQUESTS);
        /// <summary>Tool 1 requests as Excel — api/v1/HistoryExportRequests (POST).</summary>
        public static string PO_HISTORY_EXPORT_REQUESTS { get; set; } = nameof(PO_HISTORY_EXPORT_REQUESTS);
        /// <summary>All files of an upload as one Excel — api/v1/HistoryExportUpload (POST).</summary>
        public static string PO_HISTORY_EXPORT_UPLOAD { get; set; } = nameof(PO_HISTORY_EXPORT_UPLOAD);
        /// <summary>One stored data file version as Excel — api/v1/HistoryExportData (POST).</summary>
        public static string PO_HISTORY_EXPORT_DATA { get; set; } = nameof(PO_HISTORY_EXPORT_DATA);
        /// <summary>Saved Tool 2 runs — api/v1/RunsList (POST).</summary>
        public static string PO_RUNS_LIST { get; set; } = nameof(PO_RUNS_LIST);
        /// <summary>Saved Tool 2 run payload — api/v1/RunGet (POST).</summary>
        public static string PO_RUN_GET { get; set; } = nameof(PO_RUN_GET);
        /// <summary>Save a Tool 2 run payload (multipart) — api/v1/RunSave (POST).</summary>
        public static string PO_RUN_SAVE { get; set; } = nameof(PO_RUN_SAVE);
        /// <summary>Delete a saved run — api/v1/RunDelete (POST).</summary>
        public static string PO_RUN_DELETE { get; set; } = nameof(PO_RUN_DELETE);
        /// <summary>Planner parameters — api/v1/ParamsGet (POST).</summary>
        public static string PO_PARAMS_GET { get; set; } = nameof(PO_PARAMS_GET);
        /// <summary>Save planner parameters — api/v1/ParamsSave (POST).</summary>
        public static string PO_PARAMS_SAVE { get; set; } = nameof(PO_PARAMS_SAVE);
        /// <summary>A day's live Tool 1 requests — api/v1/RequestsGet (POST).</summary>
        public static string PO_REQUESTS_GET { get; set; } = nameof(PO_REQUESTS_GET);
        /// <summary>Save a Tool 1 request (409 on conflict) — api/v1/RequestSave (POST).</summary>
        public static string PO_REQUEST_SAVE { get; set; } = nameof(PO_REQUEST_SAVE);
        /// <summary>Clear a day's live ledger — api/v1/RequestsClear (POST).</summary>
        public static string PO_REQUESTS_CLEAR { get; set; } = nameof(PO_REQUESTS_CLEAR);
        /// <summary>Watch List approvals: pending + decided — api/v1/ApprovalsGet (POST).</summary>
        public static string PO_APPROVALS_GET { get; set; } = nameof(PO_APPROVALS_GET);
        /// <summary>Approve / reject a request — api/v1/ApprovalDecide (POST).</summary>
        public static string PO_APPROVAL_DECIDE { get; set; } = nameof(PO_APPROVAL_DECIDE);
        /// <summary>Users (admin) — api/v1/UsersList (POST).</summary>
        public static string PO_USERS_LIST { get; set; } = nameof(PO_USERS_LIST);
        /// <summary>SCR products file analysis (admin) — api/v1/UsersScr (POST).</summary>
        public static string PO_USERS_SCR { get; set; } = nameof(PO_USERS_SCR);
        /// <summary>Add a user (admin) — api/v1/UserAdd (POST).</summary>
        public static string PO_USER_ADD { get; set; } = nameof(PO_USER_ADD);
        /// <summary>Add several users (admin) — api/v1/UsersBulk (POST).</summary>
        public static string PO_USERS_BULK { get; set; } = nameof(PO_USERS_BULK);
        /// <summary>Create logins from the SCR file (admin) — api/v1/UsersScrSync (POST).</summary>
        public static string PO_USERS_SCR_SYNC { get; set; } = nameof(PO_USERS_SCR_SYNC);
        /// <summary>Save the SCR column mapping, then sync (admin) — api/v1/UsersScrMapping (POST).</summary>
        public static string PO_USERS_SCR_MAPPING { get; set; } = nameof(PO_USERS_SCR_MAPPING);
        /// <summary>Change name / roles / active / unlock (admin) — api/v1/UserUpdate (POST).</summary>
        public static string PO_USER_UPDATE { get; set; } = nameof(PO_USER_UPDATE);
        /// <summary>Reset a password to the default (admin) — api/v1/UserResetPassword (POST).</summary>
        public static string PO_USER_RESET_PASSWORD { get; set; } = nameof(PO_USER_RESET_PASSWORD);
        /// <summary>Delete a user (admin) — api/v1/UserDelete (POST).</summary>
        public static string PO_USER_DELETE { get; set; } = nameof(PO_USER_DELETE);
        /// <summary>Audit trail (admin) — api/v1/AuditQuery (POST).</summary>
        public static string PO_AUDIT_QUERY { get; set; } = nameof(PO_AUDIT_QUERY);
        /// <summary>Backups (admin) — api/v1/BackupsList (POST).</summary>
        public static string PO_BACKUPS_LIST { get; set; } = nameof(PO_BACKUPS_LIST);
        /// <summary>Back up now (admin) — api/v1/BackupCreate (POST).</summary>
        public static string PO_BACKUP_CREATE { get; set; } = nameof(PO_BACKUP_CREATE);
        /// <summary>Feedback items — api/v1/FeedbackList (POST).</summary>
        public static string PO_FEEDBACK_LIST { get; set; } = nameof(PO_FEEDBACK_LIST);
        /// <summary>Send feedback — api/v1/FeedbackCreate (POST).</summary>
        public static string PO_FEEDBACK_CREATE { get; set; } = nameof(PO_FEEDBACK_CREATE);
        /// <summary>Set a feedback item's status (admin) — api/v1/FeedbackSetStatus (POST).</summary>
        public static string PO_FEEDBACK_SET_STATUS { get; set; } = nameof(PO_FEEDBACK_SET_STATUS);
        /// <summary>AI provider proxy: chat (Anthropic shape) — api/v1/AiChat (POST).</summary>
        public static string PO_AI_CHAT { get; set; } = nameof(PO_AI_CHAT);
        /// <summary>AI provider proxy: insights (OpenAI shape) — api/v1/AiInsights (POST).</summary>
        public static string PO_AI_INSIGHTS { get; set; } = nameof(PO_AI_INSIGHTS);
        /// <summary>VMM Assistant panel header — api/v1/AssistantChatInfo (POST).</summary>
        public static string PO_ASSISTANT_CHAT_INFO { get; set; } = nameof(PO_ASSISTANT_CHAT_INFO);
        /// <summary>VMM Assistant question — api/v1/AssistantChat (POST).</summary>
        public static string PO_ASSISTANT_CHAT { get; set; } = nameof(PO_ASSISTANT_CHAT);
        /// <summary>AI Insights of a Tool 2 run — api/v1/AssistantInsights (POST).</summary>
        public static string PO_ASSISTANT_INSIGHTS { get; set; } = nameof(PO_ASSISTANT_INSIGHTS);
        /// <summary>Tool 1 form for a GM — api/v1/Tool1Options (POST).</summary>
        public static string PO_TOOL1_OPTIONS { get; set; } = nameof(PO_TOOL1_OPTIONS);
        /// <summary>Tool 1 Validate Request (not saved) — api/v1/Tool1Validate (POST).</summary>
        public static string PO_TOOL1_VALIDATE { get; set; } = nameof(PO_TOOL1_VALIDATE);
        /// <summary>Tool 1 decide with confirmed qty and save — api/v1/Tool1Submit (POST).</summary>
        public static string PO_TOOL1_SUBMIT { get; set; } = nameof(PO_TOOL1_SUBMIT);
        /// <summary>Today's Requests table — api/v1/Tool1Today (POST).</summary>
        public static string PO_TOOL1_TODAY { get; set; } = nameof(PO_TOOL1_TODAY);
        /// <summary>Run the PO Assessment (saves the run) — api/v1/Tool2Run (POST).</summary>
        public static string PO_TOOL2_RUN { get; set; } = nameof(PO_TOOL2_RUN);
        /// <summary>Re-open a saved Tool 2 run — api/v1/Tool2GetRun (POST).</summary>
        public static string PO_TOOL2_GET_RUN { get; set; } = nameof(PO_TOOL2_GET_RUN);
        /// <summary>Tool 2 detail view for a tab / filter — api/v1/Tool2Detail (POST).</summary>
        public static string PO_TOOL2_DETAIL { get; set; } = nameof(PO_TOOL2_DETAIL);
        /// <summary>Tool 2 Vendor and Division x MC tables — api/v1/Tool2Rollup (POST).</summary>
        public static string PO_TOOL2_ROLLUP { get; set; } = nameof(PO_TOOL2_ROLLUP);
        /// <summary>Tool 2 parameters, F2 lines, filter lists — api/v1/Tool2Options (POST).</summary>
        public static string PO_TOOL2_OPTIONS { get; set; } = nameof(PO_TOOL2_OPTIONS);
        /// <summary>Tool 2 run as Excel / CSV / zip — api/v1/Tool2Export (POST).</summary>
        public static string PO_TOOL2_EXPORT { get; set; } = nameof(PO_TOOL2_EXPORT);
        /// <summary>View Recommendation — api/v1/RecommendationGet (POST).</summary>
        public static string PO_RECOMMENDATION_GET { get; set; } = nameof(PO_RECOMMENDATION_GET);
        /// <summary>Recommendation month bar — api/v1/RecommendationMonths (POST).</summary>
        public static string PO_RECOMMENDATION_MONTHS { get; set; } = nameof(PO_RECOMMENDATION_MONTHS);
        /// <summary>Recommendation full-detail Excel — api/v1/RecommendationExport (POST).</summary>
        public static string PO_RECOMMENDATION_EXPORT { get; set; } = nameof(PO_RECOMMENDATION_EXPORT);
        /// <summary>GRC Back Testing controls — api/v1/GrcOptions (POST).</summary>
        public static string PO_GRC_OPTIONS { get; set; } = nameof(PO_GRC_OPTIONS);
        /// <summary>GRC Impact card — api/v1/GrcCompute (POST).</summary>
        public static string PO_GRC_COMPUTE { get; set; } = nameof(PO_GRC_COMPUTE);
        /// <summary>GRC Article Trace of one GM — api/v1/GrcTrace (POST).</summary>
        public static string PO_GRC_TRACE { get; set; } = nameof(PO_GRC_TRACE);
        /// <summary>GRC Row Detail Excel — api/v1/GrcExport (POST).</summary>
        public static string PO_GRC_EXPORT { get; set; } = nameof(PO_GRC_EXPORT);
        /// <summary>Attribute Analysis level 1 (Brand x MC) — api/v1/AttrAnalysisLevel1 (POST).</summary>
        public static string PO_ATTR_LEVEL1 { get; set; } = nameof(PO_ATTR_LEVEL1);
        /// <summary>Attribute Analysis level 2 (MRP x Flag) — api/v1/AttrAnalysisLevel2 (POST).</summary>
        public static string PO_ATTR_LEVEL2 { get; set; } = nameof(PO_ATTR_LEVEL2);
        /// <summary>Attribute Analysis level 3 (bucket mixes) — api/v1/AttrAnalysisLevel3 (POST).</summary>
        public static string PO_ATTR_LEVEL3 { get; set; } = nameof(PO_ATTR_LEVEL3);
        /// <summary>Category Insights dashboard — api/v1/InsightsCategory (POST).</summary>
        public static string PO_INSIGHTS_CATEGORY { get; set; } = nameof(PO_INSIGHTS_CATEGORY);
        /// <summary>Planner dashboard — api/v1/InsightsPlanner (POST).</summary>
        public static string PO_INSIGHTS_PLANNER { get; set; } = nameof(PO_INSIGHTS_PLANNER);
        /// <summary>How it works live values — api/v1/HelpParams (POST).</summary>
        public static string PO_HELP_PARAMS { get; set; } = nameof(PO_HELP_PARAMS);
        /// <summary>Season Selection — api/v1/SeasonsGet (POST).</summary>
        public static string PO_SEASONS_GET { get; set; } = nameof(PO_SEASONS_GET);
        /// <summary>Season Selection after a click — api/v1/SeasonsToggle (POST).</summary>
        public static string PO_SEASONS_TOGGLE { get; set; } = nameof(PO_SEASONS_TOGGLE);
        /// <summary>Data Setup: one data slot (multipart) — api/v1/IngestFile (POST).</summary>
        public static string PO_INGEST_FILE { get; set; } = nameof(PO_INGEST_FILE);
        /// <summary>Data Setup: zip bundle (multipart) — api/v1/IngestZip (POST).</summary>
        public static string PO_INGEST_ZIP { get; set; } = nameof(PO_INGEST_ZIP);
        /// <summary>Data Setup: raw system exports (multipart) — api/v1/IngestRaw (POST).</summary>
        public static string PO_INGEST_RAW { get; set; } = nameof(PO_INGEST_RAW);
        /// <summary>Data Setup: GRC buying-history file (multipart) — api/v1/IngestGrc (POST).</summary>
        public static string PO_INGEST_GRC { get; set; } = nameof(PO_INGEST_GRC);

        /// <summary>Copies every non-empty ApiNamesSettings value onto the property of the same name (QPS Initialize).</summary>
        public static void Initialize(ApiNames apiNames)
        {
            if (apiNames == null) return;
            foreach (var src in typeof(ApiNames).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var dst = typeof(AllApiNames).GetProperty(src.Name, BindingFlags.Public | BindingFlags.Static);
                if (dst == null || dst.PropertyType != typeof(string)) continue;
                if (src.GetValue(apiNames) is string v && v.Trim().Length > 0) dst.SetValue(null, v.Trim());
            }
        }

        /// <summary>Every task name (not MasterApi) — tests and diagnostics.</summary>
        public static IReadOnlyList<string> All() =>
            typeof(AllApiNames).GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(p => p.PropertyType == typeof(string) && p.Name != nameof(MasterApi))
                .Select(p => (string)p.GetValue(null)!).ToList();
    }
}

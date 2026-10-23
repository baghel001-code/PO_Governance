/* ═══════════════════════════════════════════════════════════════════════════
   PO Governance — API master (run in the POGovernance database, after 03_seed.sql)
     sqlcmd -S <server> -d POGovernance -E -i 04_api_master.sql
   QPS style: the portal (Admin) never builds API URLs itself. At start-up it calls GetAllApi
   (MasterApiSettings in Admin/appsettings.json) and keeps the rows of API_MASTER in AllApi.ApiDetails;
   every call is then CallingAPI(<Task_Name>, request) → POST <Api_Url> with the row's method, content
   type, timeout (seconds), proxy and Authorization (the static API token).

   * One row per POGovApi action: Task_Name = the AllApiNames / ApiNamesSettings name (PO_…),
     Api_Url = api/v1/<Action> under Base_Url_Key POGOV_API (see BASE URL PER ENVIRONMENT). POST everywhere; GET only for GetAllApi and Health.
   * Api_Authorization holds the static API token ENCODED with MyNewEncDec My_Encode — the same text as
     JwtSettings:SecretKey in POGovApi/appsettings.json and MasterApiSettings:Api_Authorization in
     Admin/appsettings.json (the portal decodes it; POGovApi compares the decoded value).
     Empty = use MasterApiSettings:Api_Authorization.
   * Api_Proxy / Api_Port / Api_Username / Api_Pwd are used when AppConfigurationSettings:ProxyValue = 1 in
     the portal (QPS proxy option).

   BASE URL PER ENVIRONMENT: API_BASE_URL holds one base URL per (Base_Url_Key, Environment). An API_MASTER row with
   a Base_Url_Key keeps only its path in Api_Url (api/v1/AuthLogin); PRO_GET_ALL_API returns <base URL of the
   environment> + <path>. Environment = ASPNETCORE_ENVIRONMENT of POGovApi (Development / Staging / Production;
   ApiMaster:Environment overrides it). A row without Base_Url_Key keeps a full URL in Api_Url, returned as is.

   The seed below only INSERTS missing rows (re-running the script never overwrites a URL, token or timeout you
   changed). Rows with full URLs from before API_BASE_URL are moved to Base_Url_Key POGOV_API once (their common base
   URL becomes the @ApiEnv base URL). Set @ApiEnv / @ApiBase / @ApiToken before the first run, or change them later:
     -- the API of one environment moved (another server / port / https):
     UPDATE dbo.API_BASE_URL SET Base_Url = N'https://pogov-api.company.local/', Active_Status = 1, Updated_At = SYSDATETIME()
     WHERE Base_Url_Key = N'POGOV_API' AND Environment = N'Production';
     -- another token for one API in one environment (My_Encode of it; '*' instead of the task = every API there):
     INSERT INTO dbo.API_AUTHORIZATION (Task_Name, Environment, Api_Authorization) VALUES (N'PO_TOOL1_VALIDATE', N'Production', N'<My_Encode(token)>');
     -- what every environment resolves to:
     SELECT Base_Url_Key, Environment, Base_Url, Active_Status FROM dbo.API_BASE_URL ORDER BY Base_Url_Key, Environment;
     -- a new static token (My_Encode of it, same value in both appsettings files):
     UPDATE dbo.API_MASTER SET Api_Authorization = N'<My_Encode(new token)>', Updated_At = SYSDATETIME();

   Then restart the portal (it reads the rows at start-up). CREATE OR ALTER / IF NOT EXISTS: safe to run again.
   ═══════════════════════════════════════════════════════════════════════════ */
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.API_MASTER', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.API_MASTER
    (
        Api_Id                  INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_API_MASTER PRIMARY KEY,
        Api_Name                NVARCHAR(100)  NOT NULL,
        Task_Name               NVARCHAR(100)  NOT NULL,
        Api_Url                 NVARCHAR(500)  NOT NULL,
        Api_Username            NVARCHAR(200)  NULL,
        Api_Pwd                 NVARCHAR(500)  NULL,
        Api_Proxy               NVARCHAR(200)  NULL,
        Api_Port                INT            NOT NULL CONSTRAINT DF_API_MASTER_Port DEFAULT (0),
        Api_Http_Method         NVARCHAR(10)   NOT NULL CONSTRAINT DF_API_MASTER_Method DEFAULT (N'POST'),
        Api_Content_Type        NVARCHAR(100)  NOT NULL CONSTRAINT DF_API_MASTER_ContentType DEFAULT (N'application/json'),
        Api_Authorization_Type  NVARCHAR(50)   NULL CONSTRAINT DF_API_MASTER_AuthType DEFAULT (N'Bearer'),
        Api_Authorization       NVARCHAR(1000) NULL,
        Api_Timeout             INT            NOT NULL CONSTRAINT DF_API_MASTER_Timeout DEFAULT (120),
        Token_Api_Name          NVARCHAR(100)  NULL,
        Api_Token_Expiry_Time   DATETIME2(0)   NULL,
        Api_Token_Status        NVARCHAR(20)   NULL,
        Active_Status           BIT            NOT NULL CONSTRAINT DF_API_MASTER_Active DEFAULT (1),
        Description             NVARCHAR(300)  NULL,
        Created_At              DATETIME2(0)   NOT NULL CONSTRAINT DF_API_MASTER_Created DEFAULT (SYSDATETIME()),
        Updated_At              DATETIME2(0)   NULL,
        CONSTRAINT UQ_API_MASTER_Task_Name UNIQUE (Task_Name),
        CONSTRAINT CK_API_MASTER_Method CHECK (Api_Http_Method IN (N'GET', N'POST', N'PUT', N'PATCH', N'DELETE')),
        CONSTRAINT CK_API_MASTER_Timeout CHECK (Api_Timeout > 0)
    );
END
GO
/* Base URL of each API host per environment (Environment = ASPNETCORE_ENVIRONMENT of POGovApi). */
IF OBJECT_ID(N'dbo.API_BASE_URL', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.API_BASE_URL
    (
        Base_Url_Id     INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_API_BASE_URL PRIMARY KEY,
        Base_Url_Key    NVARCHAR(50)  NOT NULL,      -- e.g. POGOV_API (API_MASTER.Base_Url_Key points to it)
        Environment     NVARCHAR(30)  NOT NULL,      -- Development / Staging / Production
        Base_Url        NVARCHAR(400) NOT NULL,      -- http(s)://host:port/ (a missing final / is added)
        Active_Status   BIT           NOT NULL CONSTRAINT DF_API_BASE_URL_Active DEFAULT (1),
        Description     NVARCHAR(300) NULL,
        Created_At      DATETIME2(0)  NOT NULL CONSTRAINT DF_API_BASE_URL_Created DEFAULT (SYSDATETIME()),
        Updated_At      DATETIME2(0)  NULL,
        CONSTRAINT UQ_API_BASE_URL_Key_Env UNIQUE (Base_Url_Key, Environment),
        CONSTRAINT CK_API_BASE_URL_Url CHECK (Base_Url LIKE N'http://%' OR Base_Url LIKE N'https://%')
    );
END
GO
IF COL_LENGTH(N'dbo.API_MASTER', N'Base_Url_Key') IS NULL
    ALTER TABLE dbo.API_MASTER ADD Base_Url_Key NVARCHAR(50) NULL;   -- API_BASE_URL.Base_Url_Key; NULL = Api_Url is a full URL
GO
/* Authorization token per API per environment. Task_Name = an API_MASTER task, or '*' = every API of the environment
   (except PO_GET_ALL_API, which always uses the start-up token MasterApiSettings:Api_Authorization = JwtSettings:SecretKey).
   Resolution: the task's row > the '*' row > API_MASTER.Api_Authorization. Api_Authorization = My_Encode of the token. */
IF OBJECT_ID(N'dbo.API_AUTHORIZATION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.API_AUTHORIZATION
    (
        Auth_Id                 INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_API_AUTHORIZATION PRIMARY KEY,
        Task_Name               NVARCHAR(100)  NOT NULL,     -- API_MASTER.Task_Name or '*'
        Environment             NVARCHAR(30)   NOT NULL,     -- Development / Staging / Production
        Api_Authorization_Type  NVARCHAR(50)   NULL,         -- NULL = API_MASTER.Api_Authorization_Type (Bearer)
        Api_Authorization       NVARCHAR(1000) NOT NULL,     -- My_Encode(token)
        Active_Status           BIT            NOT NULL CONSTRAINT DF_API_AUTHORIZATION_Active DEFAULT (1),
        Description             NVARCHAR(300)  NULL,
        Created_At              DATETIME2(0)   NOT NULL CONSTRAINT DF_API_AUTHORIZATION_Created DEFAULT (SYSDATETIME()),
        Updated_At              DATETIME2(0)   NULL,
        CONSTRAINT UQ_API_AUTHORIZATION_Task_Env UNIQUE (Task_Name, Environment)
    );
END
GO

/* The API master list, with the URLs of one environment.
   @Cmd ALL     (default) -> every active row, ordered by Task_Name
        BY_TASK           -> the active row of @Task_Name (QPS GetSpecificApiData; @Parm is accepted and ignored)
   @Environment             Development / Staging / Production (NULL = Production). Api_Url of a row with a Base_Url_Key =
                            the active API_BASE_URL base URL of that key and environment + the row's path; without a key = Api_Url.
   @Error_Code / @Error_Message OUTPUT: 0 / '' = success; 50001 unknown @Cmd; 50404 no row for @Task_Name;
   50424 a row needs a base URL that has no active API_BASE_URL row for @Environment.
   Api_Authorization(_Type) = the API_AUTHORIZATION row of the task and @Environment, else its '*' row, else API_MASTER's;
   Auth_Source = TASK / ENVIRONMENT / MASTER says which (POGovApi accepts per path the token resolved here). */
CREATE OR ALTER PROCEDURE dbo.PRO_GET_ALL_API
    @Cmd            NVARCHAR(20)  = NULL,
    @Task_Name      NVARCHAR(100) = NULL,
    @Parm           NVARCHAR(200) = NULL,
    @Environment    NVARCHAR(30)  = NULL,
    @Error_Code     INT = NULL OUTPUT,
    @Error_Message  NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        IF @Cmd IS NULL SET @Cmd = CASE WHEN @Task_Name IS NULL THEN N'ALL' ELSE N'BY_TASK' END;
        IF @Cmd NOT IN (N'ALL', N'BY_TASK') THROW 50001, N'PRO_GET_ALL_API: unknown @Cmd.', 1;
        SET @Environment = ISNULL(NULLIF(LTRIM(RTRIM(@Environment)), N''), N'Production');

        DECLARE @rows TABLE (Task_Name NVARCHAR(100) PRIMARY KEY, Base_Url_Key NVARCHAR(50) NULL);
        INSERT INTO @rows (Task_Name, Base_Url_Key)
        SELECT Task_Name, Base_Url_Key FROM dbo.API_MASTER
        WHERE Active_Status = 1 AND (@Cmd = N'ALL' OR Task_Name = @Task_Name);
        IF @Cmd = N'BY_TASK' AND NOT EXISTS (SELECT 1 FROM @rows)
        BEGIN
            SELECT @Error_Code = 50404, @Error_Message = N'No active API_MASTER row for task ' + ISNULL(@Task_Name, N'(null)');
            RETURN;
        END

        DECLARE @missing NVARCHAR(400);
        SELECT @missing = STRING_AGG(k.Base_Url_Key, N', ')
        FROM (SELECT DISTINCT r.Base_Url_Key FROM @rows r
              WHERE r.Base_Url_Key IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM dbo.API_BASE_URL b
                                WHERE b.Base_Url_Key = r.Base_Url_Key AND b.Environment = @Environment AND b.Active_Status = 1)) k;
        IF @missing IS NOT NULL
        BEGIN
            SELECT @Error_Code = 50424,
                   @Error_Message = LEFT(N'No active base URL in API_BASE_URL for environment ' + @Environment + N' (Base_Url_Key ' + @missing +
                                         N') - add or activate it, or check ASPNETCORE_ENVIRONMENT / ApiMaster:Environment of POGovApi', 500);
            RETURN;
        END

        SELECT m.Api_Name, m.Task_Name,
               CASE WHEN m.Base_Url_Key IS NULL THEN m.Api_Url
                    ELSE CASE WHEN RIGHT(b.Base_Url, 1) = N'/' THEN b.Base_Url ELSE b.Base_Url + N'/' END
                         + CASE WHEN LEFT(m.Api_Url, 1) = N'/' THEN STUFF(m.Api_Url, 1, 1, N'') ELSE m.Api_Url END
               END AS Api_Url,
               m.Api_Username, m.Api_Pwd, m.Api_Proxy, m.Api_Port, m.Api_Http_Method,
               m.Api_Content_Type,
               COALESCE(t.Api_Authorization_Type, m.Api_Authorization_Type) AS Api_Authorization_Type,
               COALESCE(t.Api_Authorization, m.Api_Authorization) AS Api_Authorization,
               m.Api_Timeout, m.Token_Api_Name, m.Api_Token_Expiry_Time, m.Api_Token_Status, m.Active_Status,
               ISNULL(t.Src, N'MASTER') AS Auth_Source
        FROM dbo.API_MASTER m
        JOIN @rows r ON r.Task_Name = m.Task_Name
        LEFT JOIN dbo.API_BASE_URL b ON b.Base_Url_Key = m.Base_Url_Key AND b.Environment = @Environment AND b.Active_Status = 1
        OUTER APPLY (SELECT TOP 1 a.Api_Authorization_Type, a.Api_Authorization,
                            CASE WHEN a.Task_Name = N'*' THEN N'ENVIRONMENT' ELSE N'TASK' END AS Src
                     FROM dbo.API_AUTHORIZATION a
                     WHERE a.Environment = @Environment AND a.Active_Status = 1 AND m.Task_Name <> N'PO_GET_ALL_API'
                       AND (a.Task_Name = m.Task_Name OR a.Task_Name = N'*')
                     ORDER BY CASE WHEN a.Task_Name = N'*' THEN 1 ELSE 0 END) t
        ORDER BY m.Task_Name;
    END TRY
    BEGIN CATCH
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* Seed: one row per POGovApi portal action (91; the SAP-facing endpoints under /api/sap/v1 are called by SAP directly, not listed). Only missing task names are inserted. */
DECLARE @ApiEnv   NVARCHAR(30)   = N'Development';                 -- environment of the server this script is run for
DECLARE @ApiBase  NVARCHAR(400)  = N'http://localhost:5080/';      -- base URL of POGovApi in @ApiEnv (ends with /)
DECLARE @ApiToken NVARCHAR(1000) = N'7VmtFgfO7g5YDVUW175UDqe7FIOUFq8cxpTI17P0fJiSHbePHN';   -- My_Encode(static API token) = JwtSettings:SecretKey
IF RIGHT(@ApiBase, 1) <> N'/' SET @ApiBase = @ApiBase + N'/';
IF @ApiEnv IS NULL OR @ApiEnv = N'' SET @ApiEnv = N'Development';

DECLARE @seed TABLE (Api_Name NVARCHAR(100), Task_Name NVARCHAR(100), Action_Name NVARCHAR(100),
                     Api_Http_Method NVARCHAR(10), Api_Content_Type NVARCHAR(100), Api_Timeout INT, Description NVARCHAR(300));
INSERT INTO @seed (Api_Name, Task_Name, Action_Name, Api_Http_Method, Api_Content_Type, Api_Timeout, Description) VALUES
        (N'GetAllApi', N'PO_GET_ALL_API', N'GetAllApi', N'GET', N'application/json', 60, N'API master: every active API_MASTER row (portal start-up)'),
        (N'GetSpecificApi', N'PO_GET_SPECIFIC_API', N'GetSpecificApi', N'POST', N'application/json', 60, N'API master: one row by task name'),
        (N'Health', N'PO_HEALTH', N'Health', N'GET', N'application/json', 30, N'Health check (no tokens needed)'),
        (N'AuthLogin', N'PO_AUTH_LOGIN', N'AuthLogin', N'POST', N'application/json', 60, N'Sign in: returns the session token'),
        (N'AuthLogout', N'PO_AUTH_LOGOUT', N'AuthLogout', N'POST', N'application/json', 60, N'Sign out: ends the session'),
        (N'AuthChangePassword', N'PO_AUTH_CHANGE_PASSWORD', N'AuthChangePassword', N'POST', N'application/json', 60, N'Change own password (signs out other sessions)'),
        (N'AuthMe', N'PO_AUTH_ME', N'AuthMe', N'POST', N'application/json', 60, N'The signed-in user + password rules (session check)'),
        (N'GetStatus', N'PO_GET_STATUS', N'GetStatus', N'POST', N'application/json', 60, N'Versions, datasets, params, pending approvals, AI flags'),
        (N'DataGet', N'PO_DATA_GET', N'DataGet', N'POST', N'application/json', 300, N'Stored payload of a data file version'),
        (N'DataPut', N'PO_DATA_PUT', N'DataPut', N'POST', N'multipart/form-data', 300, N'Store a new data file version (multipart, v1.1.4 rows JSON)'),
        (N'DataClear', N'PO_DATA_CLEAR', N'DataClear', N'POST', N'application/json', 120, N'Clear the current version of a data file'),
        (N'DataRestore', N'PO_DATA_RESTORE', N'DataRestore', N'POST', N'application/json', 120, N'Make an older data file version current'),
        (N'UploadStart', N'PO_UPLOAD_START', N'UploadStart', N'POST', N'application/json', 60, N'Start an upload batch'),
        (N'UploadRestore', N'PO_UPLOAD_RESTORE', N'UploadRestore', N'POST', N'application/json', 120, N'Make every file of an upload current again'),
        (N'HistoryUploads', N'PO_HISTORY_UPLOADS', N'HistoryUploads', N'POST', N'application/json', 120, N'Upload history'),
        (N'HistoryRequests', N'PO_HISTORY_REQUESTS', N'HistoryRequests', N'POST', N'application/json', 120, N'Tool 1 request history'),
        (N'HistoryExportRequests', N'PO_HISTORY_EXPORT_REQUESTS', N'HistoryExportRequests', N'POST', N'application/json', 300, N'Tool 1 requests as Excel'),
        (N'HistoryExportUpload', N'PO_HISTORY_EXPORT_UPLOAD', N'HistoryExportUpload', N'POST', N'application/json', 300, N'All files of an upload as one Excel'),
        (N'HistoryExportData', N'PO_HISTORY_EXPORT_DATA', N'HistoryExportData', N'POST', N'application/json', 300, N'One stored data file version as Excel'),
        (N'RunsList', N'PO_RUNS_LIST', N'RunsList', N'POST', N'application/json', 120, N'Saved Tool 2 runs'),
        (N'RunGet', N'PO_RUN_GET', N'RunGet', N'POST', N'application/json', 300, N'Saved Tool 2 run payload'),
        (N'RunSave', N'PO_RUN_SAVE', N'RunSave', N'POST', N'multipart/form-data', 300, N'Save a Tool 2 run payload (multipart)'),
        (N'RunDelete', N'PO_RUN_DELETE', N'RunDelete', N'POST', N'application/json', 60, N'Delete a saved run'),
        (N'ParamsGet', N'PO_PARAMS_GET', N'ParamsGet', N'POST', N'application/json', 60, N'Planner parameters'),
        (N'ParamsSave', N'PO_PARAMS_SAVE', N'ParamsSave', N'POST', N'application/json', 60, N'Save planner parameters'),
        (N'RequestsGet', N'PO_REQUESTS_GET', N'RequestsGet', N'POST', N'application/json', 120, N'A day''s live Tool 1 requests'),
        (N'RequestSave', N'PO_REQUEST_SAVE', N'RequestSave', N'POST', N'application/json', 120, N'Save a Tool 1 request (409 on conflict)'),
        (N'RequestsClear', N'PO_REQUESTS_CLEAR', N'RequestsClear', N'POST', N'application/json', 120, N'Clear a day''s live ledger'),
        (N'ApprovalsGet', N'PO_APPROVALS_GET', N'ApprovalsGet', N'POST', N'application/json', 120, N'Watch List approvals: pending + decided'),
        (N'ApprovalDecide', N'PO_APPROVAL_DECIDE', N'ApprovalDecide', N'POST', N'application/json', 120, N'Approve / reject a request'),
        (N'UsersList', N'PO_USERS_LIST', N'UsersList', N'POST', N'application/json', 60, N'Users (admin)'),
        (N'UsersScr', N'PO_USERS_SCR', N'UsersScr', N'POST', N'application/json', 120, N'SCR products file analysis (admin)'),
        (N'UserAdd', N'PO_USER_ADD', N'UserAdd', N'POST', N'application/json', 60, N'Add a user (admin)'),
        (N'UsersBulk', N'PO_USERS_BULK', N'UsersBulk', N'POST', N'application/json', 120, N'Add several users (admin)'),
        (N'UsersScrSync', N'PO_USERS_SCR_SYNC', N'UsersScrSync', N'POST', N'application/json', 120, N'Create logins from the SCR file (admin)'),
        (N'UsersScrMapping', N'PO_USERS_SCR_MAPPING', N'UsersScrMapping', N'POST', N'application/json', 120, N'Save the SCR column mapping, then sync (admin)'),
        (N'UserUpdate', N'PO_USER_UPDATE', N'UserUpdate', N'POST', N'application/json', 60, N'Change name / roles / active / unlock (admin)'),
        (N'UserResetPassword', N'PO_USER_RESET_PASSWORD', N'UserResetPassword', N'POST', N'application/json', 60, N'Reset a password to the default (admin)'),
        (N'UserDelete', N'PO_USER_DELETE', N'UserDelete', N'POST', N'application/json', 60, N'Delete a user (admin)'),
        (N'AuditQuery', N'PO_AUDIT_QUERY', N'AuditQuery', N'POST', N'application/json', 120, N'Audit trail (admin)'),
        (N'BackupsList', N'PO_BACKUPS_LIST', N'BackupsList', N'POST', N'application/json', 60, N'Backups (admin)'),
        (N'BackupCreate', N'PO_BACKUP_CREATE', N'BackupCreate', N'POST', N'application/json', 600, N'Back up now (admin)'),
        (N'FeedbackList', N'PO_FEEDBACK_LIST', N'FeedbackList', N'POST', N'application/json', 60, N'Feedback items'),
        (N'FeedbackCreate', N'PO_FEEDBACK_CREATE', N'FeedbackCreate', N'POST', N'application/json', 60, N'Send feedback'),
        (N'FeedbackSetStatus', N'PO_FEEDBACK_SET_STATUS', N'FeedbackSetStatus', N'POST', N'application/json', 60, N'Set a feedback item''s status (admin)'),
        (N'AiChat', N'PO_AI_CHAT', N'AiChat', N'POST', N'application/json', 120, N'AI provider proxy: chat (Anthropic shape)'),
        (N'AiInsights', N'PO_AI_INSIGHTS', N'AiInsights', N'POST', N'application/json', 120, N'AI provider proxy: insights (OpenAI shape)'),
        (N'AssistantChatInfo', N'PO_ASSISTANT_CHAT_INFO', N'AssistantChatInfo', N'POST', N'application/json', 60, N'VMM Assistant panel header'),
        (N'AssistantChat', N'PO_ASSISTANT_CHAT', N'AssistantChat', N'POST', N'application/json', 180, N'VMM Assistant question'),
        (N'AssistantInsights', N'PO_ASSISTANT_INSIGHTS', N'AssistantInsights', N'POST', N'application/json', 180, N'AI Insights of a Tool 2 run'),
        (N'Tool1Options', N'PO_TOOL1_OPTIONS', N'Tool1Options', N'POST', N'application/json', 120, N'Tool 1 form for a GM'),
        (N'Tool1Validate', N'PO_TOOL1_VALIDATE', N'Tool1Validate', N'POST', N'application/json', 120, N'Tool 1 Validate Request (not saved)'),
        (N'Tool1Submit', N'PO_TOOL1_SUBMIT', N'Tool1Submit', N'POST', N'application/json', 120, N'Tool 1 decide with confirmed qty and save'),
        (N'Tool1Today', N'PO_TOOL1_TODAY', N'Tool1Today', N'POST', N'application/json', 120, N'Today''s Requests table'),
        (N'Tool2Run', N'PO_TOOL2_RUN', N'Tool2Run', N'POST', N'application/json', 600, N'Run the PO Assessment (saves the run)'),
        (N'Tool2GetRun', N'PO_TOOL2_GET_RUN', N'Tool2GetRun', N'POST', N'application/json', 300, N'Re-open a saved Tool 2 run'),
        (N'Tool2Detail', N'PO_TOOL2_DETAIL', N'Tool2Detail', N'POST', N'application/json', 300, N'Tool 2 detail view for a tab / filter'),
        (N'Tool2Rollup', N'PO_TOOL2_ROLLUP', N'Tool2Rollup', N'POST', N'application/json', 300, N'Tool 2 Vendor and Division x MC tables'),
        (N'Tool2Options', N'PO_TOOL2_OPTIONS', N'Tool2Options', N'POST', N'application/json', 120, N'Tool 2 parameters, F2 lines, filter lists'),
        (N'Tool2Export', N'PO_TOOL2_EXPORT', N'Tool2Export', N'POST', N'application/json', 600, N'Tool 2 run as Excel / CSV / zip'),
        (N'RecommendationGet', N'PO_RECOMMENDATION_GET', N'RecommendationGet', N'POST', N'application/json', 300, N'View Recommendation'),
        (N'RecommendationMonths', N'PO_RECOMMENDATION_MONTHS', N'RecommendationMonths', N'POST', N'application/json', 120, N'Recommendation month bar'),
        (N'RecommendationExport', N'PO_RECOMMENDATION_EXPORT', N'RecommendationExport', N'POST', N'application/json', 300, N'Recommendation full-detail Excel'),
        (N'GrcOptions', N'PO_GRC_OPTIONS', N'GrcOptions', N'POST', N'application/json', 300, N'GRC Back Testing controls'),
        (N'GrcCompute', N'PO_GRC_COMPUTE', N'GrcCompute', N'POST', N'application/json', 600, N'GRC Impact card'),
        (N'GrcTrace', N'PO_GRC_TRACE', N'GrcTrace', N'POST', N'application/json', 300, N'GRC Article Trace of one GM'),
        (N'GrcExport', N'PO_GRC_EXPORT', N'GrcExport', N'POST', N'application/json', 600, N'GRC Row Detail Excel'),
        (N'AttrAnalysisLevel1', N'PO_ATTR_LEVEL1', N'AttrAnalysisLevel1', N'POST', N'application/json', 300, N'Attribute Analysis level 1 (Brand x MC)'),
        (N'AttrAnalysisLevel2', N'PO_ATTR_LEVEL2', N'AttrAnalysisLevel2', N'POST', N'application/json', 300, N'Attribute Analysis level 2 (MRP x Flag)'),
        (N'AttrAnalysisLevel3', N'PO_ATTR_LEVEL3', N'AttrAnalysisLevel3', N'POST', N'application/json', 300, N'Attribute Analysis level 3 (bucket mixes)'),
        (N'InsightsCategory', N'PO_INSIGHTS_CATEGORY', N'InsightsCategory', N'POST', N'application/json', 300, N'Category Insights dashboard'),
        (N'InsightsPlanner', N'PO_INSIGHTS_PLANNER', N'InsightsPlanner', N'POST', N'application/json', 300, N'Planner dashboard'),
        (N'HelpParams', N'PO_HELP_PARAMS', N'HelpParams', N'POST', N'application/json', 60, N'How it works live values'),
        (N'SeasonsGet', N'PO_SEASONS_GET', N'SeasonsGet', N'POST', N'application/json', 120, N'Season Selection'),
        (N'SeasonsToggle', N'PO_SEASONS_TOGGLE', N'SeasonsToggle', N'POST', N'application/json', 120, N'Season Selection after a click'),
        (N'IngestFile', N'PO_INGEST_FILE', N'IngestFile', N'POST', N'multipart/form-data', 900, N'Data Setup: one data slot (multipart)'),
        (N'IngestZip', N'PO_INGEST_ZIP', N'IngestZip', N'POST', N'multipart/form-data', 900, N'Data Setup: zip bundle (multipart)'),
        (N'IngestRaw', N'PO_INGEST_RAW', N'IngestRaw', N'POST', N'multipart/form-data', 900, N'Data Setup: raw system exports (multipart)'),
        (N'IngestGrc', N'PO_INGEST_GRC', N'IngestGrc', N'POST', N'multipart/form-data', 900, N'Data Setup: GRC buying-history file (multipart)'),
        (N'SapOverview', N'PO_SAP_OVERVIEW', N'SapOverview', N'POST', N'application/json', 60, N'SAP interface: settings, feed status, decision counts'),
        (N'SapDecisionList', N'PO_SAP_DECISION_LIST', N'SapDecisionList', N'POST', N'application/json', 120, N'SAP interface: decision log'),
        (N'SapDecisionGet', N'PO_SAP_DECISION_GET', N'SapDecisionGet', N'POST', N'application/json', 60, N'SAP interface: one decision with items and JSON'),
        (N'SapEventList', N'PO_SAP_EVENT_LIST', N'SapEventList', N'POST', N'application/json', 120, N'SAP interface: events'),
        (N'SapEventGet', N'PO_SAP_EVENT_GET', N'SapEventGet', N'POST', N'application/json', 60, N'SAP interface: one event with payload'),
        (N'SapTechUserList', N'PO_SAP_TECH_USER_LIST', N'SapTechUserList', N'POST', N'application/json', 60, N'SAP interface: technical users'),
        (N'SapTechUserAdd', N'PO_SAP_TECH_USER_ADD', N'SapTechUserAdd', N'POST', N'application/json', 60, N'SAP interface: add a technical user'),
        (N'SapTechUserUpdate', N'PO_SAP_TECH_USER_UPDATE', N'SapTechUserUpdate', N'POST', N'application/json', 60, N'SAP interface: enable / disable / unlock / reset password'),
        (N'SapReasonCodeList', N'PO_SAP_REASON_CODE_LIST', N'SapReasonCodeList', N'POST', N'application/json', 60, N'SAP interface: reason codes'),
        (N'SapReasonCodeSave', N'PO_SAP_REASON_CODE_SAVE', N'SapReasonCodeSave', N'POST', N'application/json', 60, N'SAP interface: add / change an exception reason code'),
        (N'SapTestDecision', N'PO_SAP_TEST_DECISION', N'SapTestDecision', N'POST', N'application/json', 60, N'SAP interface: test decision (real decision path, logged as test)'),
        (N'SapSampleRequest', N'PO_SAP_SAMPLE_REQUEST', N'SapSampleRequest', N'POST', N'application/json', 60, N'SAP interface: sample /decisions request');

-- one-time move of full URLs (a database from before API_BASE_URL: no row has a Base_Url_Key yet): their most common
-- base URL becomes the @ApiEnv base URL. Later runs leave rows that were given a full URL on purpose alone.
DECLARE @oldBase NVARCHAR(400) = NULL;
IF NOT EXISTS (SELECT 1 FROM dbo.API_MASTER WHERE Base_Url_Key IS NOT NULL)
    SET @oldBase =
        (SELECT TOP 1 LEFT(Api_Url, CHARINDEX(N'api/v1/', Api_Url) - 1)
         FROM dbo.API_MASTER WHERE CHARINDEX(N'api/v1/', Api_Url) > 1
         GROUP BY LEFT(Api_Url, CHARINDEX(N'api/v1/', Api_Url) - 1) ORDER BY COUNT(*) DESC);
IF @oldBase IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.API_BASE_URL WHERE Base_Url_Key = N'POGOV_API' AND Environment = @ApiEnv)
        INSERT INTO dbo.API_BASE_URL (Base_Url_Key, Environment, Base_Url, Description)
        VALUES (N'POGOV_API', @ApiEnv, @oldBase, N'POGovApi (taken over from the API_MASTER URLs)');
    UPDATE dbo.API_MASTER
    SET Base_Url_Key = N'POGOV_API', Api_Url = SUBSTRING(Api_Url, LEN(@oldBase) + 1, 500), Updated_At = SYSDATETIME()
    WHERE Base_Url_Key IS NULL AND LEFT(Api_Url, LEN(@oldBase)) = @oldBase;
END
-- base URLs of POGovApi: @ApiEnv from the variables above, then the defaults (only missing rows are added).
-- Production is added inactive until its real URL is set (UPDATE dbo.API_BASE_URL … Active_Status = 1, see the header).
IF NOT EXISTS (SELECT 1 FROM dbo.API_BASE_URL WHERE Base_Url_Key = N'POGOV_API' AND Environment = @ApiEnv)
    INSERT INTO dbo.API_BASE_URL (Base_Url_Key, Environment, Base_Url, Description) VALUES (N'POGOV_API', @ApiEnv, @ApiBase, N'POGovApi');
INSERT INTO dbo.API_BASE_URL (Base_Url_Key, Environment, Base_Url, Active_Status, Description)
SELECT d.k, d.env, d.url, d.active, d.descr
FROM (VALUES (N'POGOV_API', N'Development', N'http://localhost:5080/',             1, N'POGovApi on the developer machine'),
             (N'POGOV_API', N'Staging',     N'http://192.168.0.175:8178/',         1, N'POGovApi on the staging server'),
             (N'POGOV_API', N'Production',  N'https://pogov-api.change-me.local/', 0, N'POGovApi in production - set the real URL, then Active_Status = 1')
     ) d (k, env, url, active, descr)
WHERE NOT EXISTS (SELECT 1 FROM dbo.API_BASE_URL b WHERE b.Base_Url_Key = d.k AND b.Environment = d.env);

INSERT INTO dbo.API_MASTER (Api_Name, Task_Name, Base_Url_Key, Api_Url, Api_Http_Method, Api_Content_Type, Api_Authorization_Type,
                            Api_Authorization, Api_Timeout, Active_Status, Description)
SELECT s.Api_Name, s.Task_Name, N'POGOV_API', N'api/v1/' + s.Action_Name, s.Api_Http_Method, s.Api_Content_Type, N'Bearer',
       @ApiToken, s.Api_Timeout, 1, s.Description
FROM @seed s
WHERE NOT EXISTS (SELECT 1 FROM dbo.API_MASTER m WHERE m.Task_Name = s.Task_Name);
GO

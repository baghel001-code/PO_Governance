/* ═══════════════════════════════════════════════════════════════════════════
   PO Governance — SAP R/3 interface (schema v3; run in the POGovernance database, after 04_api_master.sql)
     sqlcmd -S <server> -d POGovernance -E -i 05_sap_interface.sql
   The tool side of the SAP API specification v1.4 (/api/sap/v1: ping, reason-codes, events, decisions):
     SAP_TECH_USERS     technical users (HTTP Basic, one per SAP system, PBKDF2 hash), lockout, last call
     SAP_DECISION_LOG   one row per /decisions call: request / response JSON, body hash (idempotency), mode, verdicts,
                        HTTP status (200, or the 4xx / 503 SAP records as NOT_CHECKED), duration
     SAP_DECISION_ITEMS one row per PO item of a decision (verdict, max qty, reason, engine path, metrics)
     SAP_DECISION_SEQ   decision_id = D-yyyyMMdd-<NEXT VALUE, 6+ digits>
     SAP_EVENTS         every received event (accepted or parked), de-duplicated by msg_id, seq order flag,
                        Applied = 0 until the live open-PO / stock position tables exist (handover §8 items 6, 7, 9)
     SAP_REASON_CODES   EXCEPTION codes (GET /reason-codes, approver dropdown) and VERDICT codes (items[].reason_code)
   Procedures (QPS style: @Cmd switch, @Error_Code INT / @Error_Message NVARCHAR(500) OUTPUT, TRY/CATCH):
     PRO_SAP_TECH_USER, PRO_SAP_DECISION_SAVE, PRO_SAP_DECISION_GET, PRO_SAP_EVENTS_SAVE, PRO_SAP_EVENTS_GET,
     PRO_SAP_REASON_CODES
   Own error numbers: 50001 unknown @Cmd, 50404 not found, 50409 already exists / request_id already decided.
   schema_info version = 6 (v4: verdict link View_Token, SAP_DECISION_DOCUMENTS / PRO_SAP_DOCUMENT_SAVE;
   v5: MRA status (replaced); v6: SAP MRAs SAP_MRA_FILE / _FILE_UPLOADS / _FEED, PRO_SAP_MRA;
   v7: SAP_MRA_CONTROL (IsDailyDataUpload, Data_Version), SAP_MRA_FEED_PENDING;
   v8: SAP_DECISION_ITEMS.Working_Json, PRO_SAP_MRA DECISIONS / WORKING — MRAs linked to the tool's decisions). Idempotent (IF NOT EXISTS / CREATE OR ALTER): safe to run again.
   ═══════════════════════════════════════════════════════════════════════════ */
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ───────────────────────────── tables ───────────────────────────── */

IF OBJECT_ID(N'dbo.SAP_TECH_USERS', N'U') IS NULL
CREATE TABLE dbo.SAP_TECH_USERS (
    Id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SAP_TECH_USERS PRIMARY KEY,
    User_Name            NVARCHAR(64)  NOT NULL CONSTRAINT UQ_SAP_TECH_USERS_User_Name UNIQUE,
    Source_System        NVARCHAR(10)  NOT NULL,
    Description          NVARCHAR(200) NULL,
    Password_Hash        NVARCHAR(200) NOT NULL,
    Active               BIT NOT NULL CONSTRAINT DF_SAP_TECH_USERS_Active DEFAULT (1),
    Failed_Count         INT NOT NULL CONSTRAINT DF_SAP_TECH_USERS_Failed DEFAULT (0),
    Locked_Until         DATETIMEOFFSET(7) NULL,
    Last_Call_At         DATETIMEOFFSET(7) NULL,
    Last_Call_Ip         NVARCHAR(64)  NULL,
    Password_Changed_At  DATETIMEOFFSET(7) NULL,
    Created_At           DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_SAP_TECH_USERS_Created DEFAULT SYSDATETIMEOFFSET(),
    Created_By           NVARCHAR(200) NULL,
    Updated_At           DATETIMEOFFSET(7) NULL);
GO

IF OBJECT_ID(N'dbo.SAP_DECISION_SEQ', N'SO') IS NULL
    CREATE SEQUENCE dbo.SAP_DECISION_SEQ AS BIGINT START WITH 1 INCREMENT BY 1 NO CYCLE CACHE 50;
GO

IF OBJECT_ID(N'dbo.SAP_DECISION_LOG', N'U') IS NULL
CREATE TABLE dbo.SAP_DECISION_LOG (
    Id                   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SAP_DECISION_LOG PRIMARY KEY,
    Decision_Id          NVARCHAR(30)  NULL,
    Request_Id           NVARCHAR(80)  COLLATE Latin1_General_100_BIN2 NOT NULL,
    Body_Hash            CHAR(64)      NULL,
    Mode                 NVARCHAR(10)  NULL,
    Source_System        NVARCHAR(10)  NULL,
    Client               NVARCHAR(10)  NULL,
    Po_Number            NVARCHAR(20)  NULL,
    Buyer_User           NVARCHAR(64)  NULL,
    Division             NVARCHAR(40)  NULL,
    Overall_Verdict      NVARCHAR(10)  NULL,
    Verdict_Code         NCHAR(1)      NULL,
    Route_To             NVARCHAR(10)  NULL,
    Bm_Head_Required     BIT NOT NULL CONSTRAINT DF_SAP_DECISION_LOG_BmHead DEFAULT (0),
    Http_Status          INT NOT NULL,
    Error_Code           NVARCHAR(50)  NULL,
    Error_Message        NVARCHAR(500) NULL,
    Item_Count           INT NOT NULL CONSTRAINT DF_SAP_DECISION_LOG_Items DEFAULT (0),
    Data_Delayed         BIT NOT NULL CONSTRAINT DF_SAP_DECISION_LOG_Delayed DEFAULT (0),
    Events_Lag_Seconds   BIGINT NULL,
    Rules_Version        NVARCHAR(40)  NULL,
    Duration_Ms          INT NOT NULL CONSTRAINT DF_SAP_DECISION_LOG_Duration DEFAULT (0),
    Tech_User            NVARCHAR(200) NULL,
    Client_Ip            NVARCHAR(64)  NULL,
    Is_Test              BIT NOT NULL CONSTRAINT DF_SAP_DECISION_LOG_Test DEFAULT (0),
    Trace_Id             NVARCHAR(20)  NULL,
    Requested_At         DATETIMEOFFSET(7) NULL,
    Decided_At           DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_SAP_DECISION_LOG_Decided DEFAULT SYSDATETIMEOFFSET(),
    Request_Json         NVARCHAR(MAX) NULL,
    Response_Json        NVARCHAR(MAX) NULL CONSTRAINT CK_SAP_DECISION_LOG_Response_Json CHECK (Response_Json IS NULL OR ISJSON(Response_Json) = 1));
GO
/* idempotency: one answered (HTTP 200) decision per request_id */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SAP_DECISION_LOG_Request' AND object_id = OBJECT_ID(N'dbo.SAP_DECISION_LOG'))
    CREATE UNIQUE INDEX UX_SAP_DECISION_LOG_Request ON dbo.SAP_DECISION_LOG (Request_Id) WHERE Http_Status = 200;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SAP_DECISION_LOG_Decided' AND object_id = OBJECT_ID(N'dbo.SAP_DECISION_LOG'))
    CREATE INDEX IX_SAP_DECISION_LOG_Decided ON dbo.SAP_DECISION_LOG (Decided_At DESC) INCLUDE (Http_Status, Overall_Verdict, Mode, Is_Test, Duration_Ms);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SAP_DECISION_LOG_Po' AND object_id = OBJECT_ID(N'dbo.SAP_DECISION_LOG'))
    CREATE INDEX IX_SAP_DECISION_LOG_Po ON dbo.SAP_DECISION_LOG (Po_Number);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SAP_DECISION_LOG_DecisionId' AND object_id = OBJECT_ID(N'dbo.SAP_DECISION_LOG'))
    CREATE INDEX IX_SAP_DECISION_LOG_DecisionId ON dbo.SAP_DECISION_LOG (Decision_Id) WHERE Decision_Id IS NOT NULL;
GO

IF OBJECT_ID(N'dbo.SAP_DECISION_ITEMS', N'U') IS NULL
CREATE TABLE dbo.SAP_DECISION_ITEMS (
    Id                   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SAP_DECISION_ITEMS PRIMARY KEY,
    Decision_Log_Id      BIGINT NOT NULL,   -- SAP_DECISION_LOG.Id (no FK: a multi-row FK check scans the log under S locks and blocks parallel decisions)
    Po_Item              INT NOT NULL,
    Article              NVARCHAR(40)  NULL,
    Generic_Article      NVARCHAR(40)  NULL,
    Gm                   NVARCHAR(40)  NULL,
    Gm_Match             NVARCHAR(30)  NULL,
    Colour_Sent          NVARCHAR(60)  NULL,
    Colour               NVARCHAR(60)  NULL,
    Colour_Match         NVARCHAR(20)  NULL,
    Delivery_Month       NVARCHAR(7)   NULL,
    Order_Qty            FLOAT NOT NULL CONSTRAINT DF_SAP_DECISION_ITEMS_Qty DEFAULT (0),
    Group_Qty            FLOAT NOT NULL CONSTRAINT DF_SAP_DECISION_ITEMS_GroupQty DEFAULT (0),
    Verdict              NVARCHAR(10)  NULL,
    Verdict_Code         NCHAR(1)      NULL,
    Max_Qty              FLOAT NULL,
    Reason_Code          NVARCHAR(40)  NULL,
    Rule_Id              NVARCHAR(30)  NULL,
    Reason_Text          NVARCHAR(200) NULL,
    Engine               NVARCHAR(10)  NULL,
    Engine_Decision      NVARCHAR(40)  NULL,
    Engine_Reason        NVARCHAR(MAX) NULL,
    Metrics_Json         NVARCHAR(MAX) NULL CONSTRAINT CK_SAP_DECISION_ITEMS_Metrics_Json CHECK (Metrics_Json IS NULL OR ISJSON(Metrics_Json) = 1));
GO
IF OBJECT_ID(N'dbo.FK_SAP_DECISION_ITEMS_Log', N'F') IS NOT NULL
    ALTER TABLE dbo.SAP_DECISION_ITEMS DROP CONSTRAINT FK_SAP_DECISION_ITEMS_Log;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SAP_DECISION_ITEMS_Log' AND object_id = OBJECT_ID(N'dbo.SAP_DECISION_ITEMS'))
    CREATE INDEX IX_SAP_DECISION_ITEMS_Log ON dbo.SAP_DECISION_ITEMS (Decision_Log_Id, Po_Item);
GO
/* schema v8: the working of each decided item (same fields as "Download working (JSON)"), shown on MRA Queues for the MRA
   raised from that check; and the lookup article + colour + delivery month → decisions */
IF COL_LENGTH(N'dbo.SAP_DECISION_ITEMS', N'Working_Json') IS NULL
    ALTER TABLE dbo.SAP_DECISION_ITEMS ADD Working_Json NVARCHAR(MAX) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SAP_DECISION_ITEMS_Key' AND object_id = OBJECT_ID(N'dbo.SAP_DECISION_ITEMS'))
    CREATE INDEX IX_SAP_DECISION_ITEMS_Key ON dbo.SAP_DECISION_ITEMS (Gm, Colour, Delivery_Month) INCLUDE (Decision_Log_Id, Order_Qty);
GO

/* schema v4: verdict link (View_Token) on the decision, and the final SAP document (MRN) of a request_id */
IF COL_LENGTH(N'dbo.SAP_DECISION_LOG', N'View_Token') IS NULL
    ALTER TABLE dbo.SAP_DECISION_LOG ADD
        View_Token       NVARCHAR(64) COLLATE Latin1_General_100_BIN2 NULL,   -- random, unguessable: /verdict/<token> on the portal
        View_Expires_At  DATETIMEOFFSET(7) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SAP_DECISION_LOG_ViewToken' AND object_id = OBJECT_ID(N'dbo.SAP_DECISION_LOG'))
    CREATE UNIQUE INDEX UX_SAP_DECISION_LOG_ViewToken ON dbo.SAP_DECISION_LOG (View_Token) WHERE View_Token IS NOT NULL;
GO

/* Final SAP document (MRN) posted against an answered request_id (POST /api/sap/v1/documents). One current row per
   request_id; a correction (replace = true) keeps the old row with Is_Current = 0 and Replaced_At. */
IF OBJECT_ID(N'dbo.SAP_DECISION_DOCUMENTS', N'U') IS NULL
CREATE TABLE dbo.SAP_DECISION_DOCUMENTS (
    Id                   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SAP_DECISION_DOCUMENTS PRIMARY KEY,
    Decision_Log_Id      BIGINT NOT NULL,                                     -- SAP_DECISION_LOG.Id of the answered decision
    Request_Id           NVARCHAR(80)  COLLATE Latin1_General_100_BIN2 NOT NULL,
    Decision_Id          NVARCHAR(30)  NULL,
    Document_No          NVARCHAR(40)  NOT NULL,
    Document_Type        NVARCHAR(20)  NOT NULL CONSTRAINT DF_SAP_DECISION_DOCUMENTS_Type DEFAULT (N'MRN'),
    Po_Number            NVARCHAR(20)  NULL,
    Document_Date        DATE          NULL,
    Note                 NVARCHAR(500) NULL,
    Is_Current           BIT NOT NULL CONSTRAINT DF_SAP_DECISION_DOCUMENTS_Current DEFAULT (1),
    Replaced_At          DATETIMEOFFSET(7) NULL,
    Tech_User            NVARCHAR(200) NULL,
    Source_System        NVARCHAR(10)  NULL,
    Client_Ip            NVARCHAR(64)  NULL,
    Trace_Id             NVARCHAR(20)  NULL,
    Received_At          DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_SAP_DECISION_DOCUMENTS_Received DEFAULT SYSDATETIMEOFFSET(),
    Request_Json         NVARCHAR(MAX) NULL);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SAP_DECISION_DOCUMENTS_Current' AND object_id = OBJECT_ID(N'dbo.SAP_DECISION_DOCUMENTS'))
    CREATE UNIQUE INDEX UX_SAP_DECISION_DOCUMENTS_Current ON dbo.SAP_DECISION_DOCUMENTS (Request_Id) WHERE Is_Current = 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SAP_DECISION_DOCUMENTS_Log' AND object_id = OBJECT_ID(N'dbo.SAP_DECISION_DOCUMENTS'))
    CREATE INDEX IX_SAP_DECISION_DOCUMENTS_Log ON dbo.SAP_DECISION_DOCUMENTS (Decision_Log_Id, Is_Current);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SAP_DECISION_DOCUMENTS_No' AND object_id = OBJECT_ID(N'dbo.SAP_DECISION_DOCUMENTS'))
    CREATE INDEX IX_SAP_DECISION_DOCUMENTS_No ON dbo.SAP_DECISION_DOCUMENTS (Document_No);
GO


IF OBJECT_ID(N'dbo.SAP_EVENTS', N'U') IS NULL
CREATE TABLE dbo.SAP_EVENTS (
    Id                   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SAP_EVENTS PRIMARY KEY,
    Msg_Id               NVARCHAR(100) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT UQ_SAP_EVENTS_Msg_Id UNIQUE,
    Seq                  BIGINT NULL,
    Queue                NVARCHAR(64)  NULL,
    Source_System        NVARCHAR(10)  NULL,
    Client               NVARCHAR(10)  NULL,
    Event_Type           NVARCHAR(30)  NULL,
    Occurred_At          DATETIMEOFFSET(7) NULL,
    Received_At          DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_SAP_EVENTS_Received DEFAULT SYSDATETIMEOFFSET(),
    Sap_User             NVARCHAR(40)  NULL,
    Po_Number            NVARCHAR(20)  NULL,
    Po_Item              INT NULL,
    Material_Doc         NVARCHAR(20)  NULL,
    Decision_Id          NVARCHAR(30)  NULL,
    Status               NVARCHAR(10)  NOT NULL,
    Error_Code           NVARCHAR(50)  NULL,
    Error_Message        NVARCHAR(500) NULL,
    Seq_Status           NVARCHAR(10)  NULL,
    Applied              BIT NOT NULL CONSTRAINT DF_SAP_EVENTS_Applied DEFAULT (0),
    Applied_At           DATETIMEOFFSET(7) NULL,
    Dup_Count            INT NOT NULL CONSTRAINT DF_SAP_EVENTS_Dup DEFAULT (0),
    Last_Dup_At          DATETIMEOFFSET(7) NULL,
    Tech_User            NVARCHAR(64)  NULL,
    Batch_Id             NVARCHAR(40)  NULL,
    Payload_Json         NVARCHAR(MAX) NOT NULL CONSTRAINT CK_SAP_EVENTS_Payload_Json CHECK (ISJSON(Payload_Json) = 1),
    CONSTRAINT CK_SAP_EVENTS_Status CHECK (Status IN (N'ACCEPTED', N'PARKED')));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SAP_EVENTS_Queue_Seq' AND object_id = OBJECT_ID(N'dbo.SAP_EVENTS'))
    CREATE INDEX IX_SAP_EVENTS_Queue_Seq ON dbo.SAP_EVENTS (Queue, Seq) WHERE Status = N'ACCEPTED';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SAP_EVENTS_Received' AND object_id = OBJECT_ID(N'dbo.SAP_EVENTS'))
    CREATE INDEX IX_SAP_EVENTS_Received ON dbo.SAP_EVENTS (Received_At DESC) INCLUDE (Status, Event_Type, Applied);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SAP_EVENTS_Po' AND object_id = OBJECT_ID(N'dbo.SAP_EVENTS'))
    CREATE INDEX IX_SAP_EVENTS_Po ON dbo.SAP_EVENTS (Po_Number, Po_Item);
GO

IF OBJECT_ID(N'dbo.SAP_REASON_CODES', N'U') IS NULL
CREATE TABLE dbo.SAP_REASON_CODES (
    Code_Type            NVARCHAR(10)  NOT NULL,
    Code                 NVARCHAR(40)  NOT NULL,
    Text                 NVARCHAR(200) NOT NULL,
    Active               BIT NOT NULL CONSTRAINT DF_SAP_REASON_CODES_Active DEFAULT (1),
    Sort_Order           INT NOT NULL CONSTRAINT DF_SAP_REASON_CODES_Sort DEFAULT (0),
    Verdict              NVARCHAR(20)  NULL,
    Rule_Ids             NVARCHAR(200) NULL,
    Updated_At           DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_SAP_REASON_CODES_Updated DEFAULT SYSDATETIMEOFFSET(),
    Updated_By           NVARCHAR(200) NULL,
    CONSTRAINT PK_SAP_REASON_CODES PRIMARY KEY (Code_Type, Code),
    CONSTRAINT CK_SAP_REASON_CODES_Type CHECK (Code_Type IN (N'EXCEPTION', N'VERDICT')),
    -- spec ReasonCode: code maxLength 4, text maxLength 60 (exception codes only)
    CONSTRAINT CK_SAP_REASON_CODES_Exception CHECK (Code_Type <> N'EXCEPTION' OR (LEN(Code) <= 4 AND LEN(Text) <= 60)));
GO

/* ───────────────────────────── seed ───────────────────────────── */

/* Exception reason codes: the starting proposal of spec v1.4 §8 ("to be confirmed"; maintained by the planning
   head on the portal, /admin/sap → Reason codes). Only missing codes are inserted. */
INSERT INTO dbo.SAP_REASON_CODES (Code_Type, Code, Text, Active, Sort_Order, Updated_By)
SELECT s.Code_Type, s.Code, s.Text, 1, s.Sort_Order, N'seed'
FROM (VALUES
    (N'EXCEPTION', N'NEWL', N'New launch - no sales history', 10),
    (N'EXCEPTION', N'FEST', N'Festive / event build-up', 20),
    (N'EXCEPTION', N'VMOQ', N'Vendor minimum order quantity', 30),
    (N'EXCEPTION', N'PRCA', N'Price advantage / one-time buy', 40),
    (N'EXCEPTION', N'REPL', N'Store replenishment gap', 50),
    (N'EXCEPTION', N'OTHR', N'Other (note mandatory)', 60)
) AS s (Code_Type, Code, Text, Sort_Order)
WHERE NOT EXISTS (SELECT 1 FROM dbo.SAP_REASON_CODES r WHERE r.Code_Type = s.Code_Type AND r.Code = s.Code);
GO

/* Verdict reason codes (items[].reason_code / rule_id of /decisions) = DecisionEngine/Sap/SapVerdictMap.Table
   (tests/POGovernance.Tests/Sap checks that both lists are the same). Texts are refreshed on every run. */
MERGE dbo.SAP_REASON_CODES AS t
USING (VALUES
    (N'COVER_WITHIN_NORM', N'Cover within the norm (STR <= T + buffer)', N'ALLOW', N'COVER-00', 110),
    (N'NO_PLAN_DATA', N'No Plan Available: no F4 plan for the MC-MRP-Flag in the requirement window, ask the planning team to add it', N'HOLD', N'PLAN-00', 120),
    (N'COVER_ABOVE_NORM', N'Cover above the norm: reduce to Max Permissible', N'REDUCE', N'COVER-01', 130),
    (N'COVER_NO_HEADROOM', N'Existing stock + pending already above the norm: Max Permissible 0', N'HOLD', N'COVER-02', 140),
    (N'MIX_BELOW_MIN', N'Colour mix below the minimum share of its MC-MRP', N'WATCH', N'MIX-01', 150),
    (N'PEER_Q1', N'Ranks Q1 in its BSR peer group', N'ALLOW', N'PEER-Q1', 160),
    (N'PEER_Q2', N'Ranks Q2 in its BSR peer group', N'ALLOW', N'PEER-Q2', 170),
    (N'PEER_Q3', N'Ranks Q3: ALLOW at or near the peer average, WATCH below it beyond the border zone', N'ALLOW/WATCH', N'PEER-Q3-OK,PEER-Q3-GAP', 180),
    (N'PEER_Q4', N'Ranks Q4 but at or near the peer average', N'WATCH', N'PEER-Q4-WATCH', 190),
    (N'BLOCK', N'Block: confirmed slow seller (Q4 beyond the border zone, or sell-thru below 1 %/day after 4+ weeks)', N'HOLD|BLOCK', N'BLOCK-Q4,BLOCK-ABS', 200),
    (N'BOBS_SIBLINGS_OK', N'New colour: all sibling colours rank Q1/Q2', N'ALLOW', N'BOBS-01', 210),
    (N'BOBS_SIBLING_LOW', N'New colour: a sibling colour ranks Q3/Q4', N'WATCH', N'BOBS-02', 220),
    (N'LOW_HISTORY', N'Too little selling history (weeks on floor below the gate), allowed', N'ALLOW', N'WOF-00', 230),
    (N'NO_PEER_POOL', N'No BSR peer group available, allowed', N'ALLOW', N'PEER-00', 240),
    (N'NEW_ARTICLE', N'New article: not in Universe Master or BSR', N'ALLOW', N'NEW-00', 250),
    (N'CANNOT_VALIDATE', N'The tool cannot validate this item (unknown article / missing data)', N'WATCH', N'CV-00', 260)
) AS s (Code, Text, Verdict, Rule_Ids, Sort_Order)
ON t.Code_Type = N'VERDICT' AND t.Code = s.Code
WHEN MATCHED THEN UPDATE SET Text = s.Text, Verdict = s.Verdict, Rule_Ids = s.Rule_Ids, Sort_Order = s.Sort_Order, Updated_At = SYSDATETIMEOFFSET(), Updated_By = N'seed'
WHEN NOT MATCHED THEN INSERT (Code_Type, Code, Text, Active, Sort_Order, Verdict, Rule_Ids, Updated_By)
     VALUES (N'VERDICT', s.Code, s.Text, 1, s.Sort_Order, s.Verdict, s.Rule_Ids, N'seed');
GO

/* ───────────────────────────── technical users ───────────────────────────── */

/* @Cmd GET          -> the row of @User_Name (no row = unknown user)
        LIST         -> every row ordered by User_Name
        ADD          -> insert (@User_Name, @Source_System, @Description, @Password_Hash, @By); 50409 when the name exists
        SET_PASSWORD -> new @Password_Hash, unlocks; 50404 when unknown
        SET_ACTIVE   -> Active = @Active (and unlocks when enabling); 50404 when unknown
        UNLOCK       -> Failed_Count = 0, Locked_Until = NULL
        LOGIN_OK     -> successful Basic login from @Ip: resets the failure count, sets Last_Call_At / Last_Call_Ip
        LOGIN_FAIL   -> failed login: Failed_Count + 1; locked for @Lockout_Minutes once it reaches @Max_Failed; returns the row
   Every command except LIST / LOGIN_* returns the (changed) row. */
CREATE OR ALTER PROCEDURE dbo.PRO_SAP_TECH_USER
    @Cmd              NVARCHAR(20),
    @User_Name        NVARCHAR(64)  = NULL,
    @Source_System    NVARCHAR(10)  = NULL,
    @Description      NVARCHAR(200) = NULL,
    @Password_Hash    NVARCHAR(200) = NULL,
    @Active           BIT = NULL,
    @Ip               NVARCHAR(64)  = NULL,
    @Max_Failed       INT = 5,
    @Lockout_Minutes  INT = 15,
    @By               NVARCHAR(200) = NULL,
    @Error_Code       INT = NULL OUTPUT,
    @Error_Message    NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        DECLARE @now DATETIMEOFFSET(7) = SYSDATETIMEOFFSET();
        IF @Cmd = N'LIST'
        BEGIN
            SELECT Id, User_Name AS UserName, Source_System AS SourceSystem, Description, Password_Hash AS PasswordHash, Active,
                   Failed_Count AS FailedCount, Locked_Until AS LockedUntil, Last_Call_At AS LastCallAt, Last_Call_Ip AS LastCallIp,
                   Password_Changed_At AS PasswordChangedAt, Created_At AS CreatedAt, Created_By AS CreatedBy
              FROM dbo.SAP_TECH_USERS ORDER BY User_Name;
            RETURN;
        END
        IF @Cmd = N'ADD'
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.SAP_TECH_USERS WHERE User_Name = @User_Name)
                THROW 50409, N'A technical user with this name already exists.', 1;
            INSERT INTO dbo.SAP_TECH_USERS (User_Name, Source_System, Description, Password_Hash, Active, Password_Changed_At, Created_By)
            VALUES (@User_Name, UPPER(@Source_System), @Description, @Password_Hash, 1, @now, @By);
        END
        ELSE IF @Cmd = N'SET_PASSWORD'
        BEGIN
            UPDATE dbo.SAP_TECH_USERS SET Password_Hash = @Password_Hash, Password_Changed_At = @now, Failed_Count = 0, Locked_Until = NULL, Updated_At = @now
             WHERE User_Name = @User_Name;
            IF @@ROWCOUNT = 0 THROW 50404, N'Unknown technical user.', 1;
        END
        ELSE IF @Cmd = N'SET_ACTIVE'
        BEGIN
            UPDATE dbo.SAP_TECH_USERS
               SET Active = @Active, Updated_At = @now,
                   Failed_Count = CASE WHEN @Active = 1 THEN 0 ELSE Failed_Count END,
                   Locked_Until = CASE WHEN @Active = 1 THEN NULL ELSE Locked_Until END
             WHERE User_Name = @User_Name;
            IF @@ROWCOUNT = 0 THROW 50404, N'Unknown technical user.', 1;
        END
        ELSE IF @Cmd = N'UNLOCK'
        BEGIN
            UPDATE dbo.SAP_TECH_USERS SET Failed_Count = 0, Locked_Until = NULL, Updated_At = @now WHERE User_Name = @User_Name;
            IF @@ROWCOUNT = 0 THROW 50404, N'Unknown technical user.', 1;
        END
        ELSE IF @Cmd = N'LOGIN_OK'
        BEGIN
            UPDATE dbo.SAP_TECH_USERS SET Failed_Count = 0, Locked_Until = NULL, Last_Call_At = @now, Last_Call_Ip = @Ip
             WHERE User_Name = @User_Name;
            RETURN;
        END
        ELSE IF @Cmd = N'LOGIN_FAIL'
        BEGIN
            UPDATE dbo.SAP_TECH_USERS
               SET Failed_Count = Failed_Count + 1,
                   Locked_Until = CASE WHEN Failed_Count + 1 >= @Max_Failed THEN DATEADD(MINUTE, @Lockout_Minutes, @now) ELSE Locked_Until END
             WHERE User_Name = @User_Name;
        END
        ELSE IF @Cmd <> N'GET'
            THROW 50001, N'PRO_SAP_TECH_USER: unknown @Cmd.', 1;

        SELECT Id, User_Name AS UserName, Source_System AS SourceSystem, Description, Password_Hash AS PasswordHash, Active,
               Failed_Count AS FailedCount, Locked_Until AS LockedUntil, Last_Call_At AS LastCallAt, Last_Call_Ip AS LastCallIp,
               Password_Changed_At AS PasswordChangedAt, Created_At AS CreatedAt, Created_By AS CreatedBy
          FROM dbo.SAP_TECH_USERS WHERE User_Name = @User_Name;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* ───────────────────────────── decisions ───────────────────────────── */

/* @Cmd NEXT_ID -> @Seq_No OUTPUT = NEXT VALUE FOR SAP_DECISION_SEQ (the API formats D-yyyyMMdd-000001)
        SET_VIEW_TOKEN -> @View_Token / @View_Expires_At / @Response_Json on the answered row @Log_Id that has no link yet
                   (50409 when it already has one)
        INSERT  -> one SAP_DECISION_LOG row + its items (@Items = JSON array, see SAP_DECISION_ITEMS columns), one
                   transaction; @Log_Id OUTPUT. An HTTP 200 row whose @Request_Id already has an answered decision is
                   refused with 50409 (the caller re-reads it: concurrent duplicate request). */
CREATE OR ALTER PROCEDURE dbo.PRO_SAP_DECISION_SAVE
    @Cmd                 NVARCHAR(20),
    @Decision_Id         NVARCHAR(30)  = NULL,
    @Request_Id          NVARCHAR(80)  = NULL,
    @Body_Hash           CHAR(64)      = NULL,
    @Mode                NVARCHAR(10)  = NULL,
    @Source_System       NVARCHAR(10)  = NULL,
    @Client              NVARCHAR(10)  = NULL,
    @Po_Number           NVARCHAR(20)  = NULL,
    @Buyer_User          NVARCHAR(64)  = NULL,
    @Division            NVARCHAR(40)  = NULL,
    @Overall_Verdict     NVARCHAR(10)  = NULL,
    @Verdict_Code        NCHAR(1)      = NULL,
    @Route_To            NVARCHAR(10)  = NULL,
    @Bm_Head_Required    BIT = 0,
    @Http_Status         INT = 200,
    @Error_Code_Out      NVARCHAR(50)  = NULL,
    @Error_Text          NVARCHAR(500) = NULL,
    @Item_Count          INT = 0,
    @Data_Delayed        BIT = 0,
    @Events_Lag_Seconds  BIGINT = NULL,
    @Rules_Version       NVARCHAR(40)  = NULL,
    @Duration_Ms         INT = 0,
    @Tech_User           NVARCHAR(200) = NULL,
    @Client_Ip           NVARCHAR(64)  = NULL,
    @Is_Test             BIT = 0,
    @Trace_Id            NVARCHAR(20)  = NULL,
    @Requested_At        DATETIMEOFFSET(7) = NULL,
    @Decided_At          DATETIMEOFFSET(7) = NULL,
    @Request_Json        NVARCHAR(MAX) = NULL,
    @Response_Json       NVARCHAR(MAX) = NULL,
    @View_Token          NVARCHAR(64)  = NULL,
    @View_Expires_At     DATETIMEOFFSET(7) = NULL,
    @Items               NVARCHAR(MAX) = NULL,
    @Seq_No              BIGINT = NULL OUTPUT,
    @Log_Id              BIGINT = NULL OUTPUT,
    @Error_Code          INT = NULL OUTPUT,
    @Error_Message       NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        IF @Cmd = N'NEXT_ID'
        BEGIN
            SET @Seq_No = NEXT VALUE FOR dbo.SAP_DECISION_SEQ;
            RETURN;
        END
        IF @Cmd = N'SET_VIEW_TOKEN'
        BEGIN
            /* a decision answered before the verdict link existed gets one on its first replay (only if it still has none) */
            UPDATE dbo.SAP_DECISION_LOG SET View_Token = @View_Token, View_Expires_At = @View_Expires_At, Response_Json = @Response_Json
             WHERE Id = @Log_Id AND View_Token IS NULL AND Http_Status = 200;
            IF @@ROWCOUNT = 0 SELECT @Error_Code = 50409, @Error_Message = N'The decision already has a verdict link.';
            RETURN;
        END
        IF @Cmd <> N'INSERT' THROW 50001, N'PRO_SAP_DECISION_SAVE: unknown @Cmd.', 1;

        /* No range lock here (it serialises — and deadlocks — parallel SAP calls): the filtered unique index
           UX_SAP_DECISION_LOG_Request refuses a second answered row for the request_id (2601 -> 50409 below). */
        BEGIN TRANSACTION;
        INSERT INTO dbo.SAP_DECISION_LOG (Decision_Id, Request_Id, Body_Hash, Mode, Source_System, Client, Po_Number, Buyer_User, Division,
                    Overall_Verdict, Verdict_Code, Route_To, Bm_Head_Required, Http_Status, Error_Code, Error_Message, Item_Count, Data_Delayed,
                    Events_Lag_Seconds, Rules_Version, Duration_Ms, Tech_User, Client_Ip, Is_Test, Trace_Id, Requested_At, Decided_At,
                    Request_Json, Response_Json, View_Token, View_Expires_At)
        VALUES (@Decision_Id, ISNULL(@Request_Id, N''), @Body_Hash, @Mode, @Source_System, @Client, @Po_Number, @Buyer_User, @Division,
                @Overall_Verdict, @Verdict_Code, @Route_To, ISNULL(@Bm_Head_Required, 0), @Http_Status, @Error_Code_Out, @Error_Text,
                ISNULL(@Item_Count, 0), ISNULL(@Data_Delayed, 0), @Events_Lag_Seconds, @Rules_Version, ISNULL(@Duration_Ms, 0), @Tech_User,
                @Client_Ip, ISNULL(@Is_Test, 0), @Trace_Id, @Requested_At, ISNULL(@Decided_At, SYSDATETIMEOFFSET()), @Request_Json, @Response_Json, @View_Token, @View_Expires_At);
        SET @Log_Id = SCOPE_IDENTITY();

        INSERT INTO dbo.SAP_DECISION_ITEMS (Decision_Log_Id, Po_Item, Article, Generic_Article, Gm, Gm_Match, Colour_Sent, Colour, Colour_Match,
                    Delivery_Month, Order_Qty, Group_Qty, Verdict, Verdict_Code, Max_Qty, Reason_Code, Rule_Id, Reason_Text, Engine,
                    Engine_Decision, Engine_Reason, Metrics_Json, Working_Json)
        SELECT @Log_Id, j.PoItem, j.Article, j.GenericArticle, j.Gm, j.GmMatch, j.ColourSent, j.Colour, j.ColourMatch, j.DeliveryMonth,
               ISNULL(j.OrderQty, 0), ISNULL(j.GroupQty, 0), j.Verdict, j.VerdictCode, j.MaxQty, j.ReasonCode, j.RuleId, LEFT(j.ReasonText, 200),
               j.Engine, LEFT(j.EngineDecision, 40), j.EngineReason, j.MetricsJson, j.WorkingJson
          FROM OPENJSON(ISNULL(@Items, N'[]')) AS a
         CROSS APPLY OPENJSON(a.value) WITH (
                PoItem INT N'$.PoItem', Article NVARCHAR(40) N'$.Article', GenericArticle NVARCHAR(40) N'$.GenericArticle',
                Gm NVARCHAR(40) N'$.Gm', GmMatch NVARCHAR(30) N'$.GmMatch', ColourSent NVARCHAR(60) N'$.ColourSent',
                Colour NVARCHAR(60) N'$.Colour', ColourMatch NVARCHAR(20) N'$.ColourMatch', DeliveryMonth NVARCHAR(7) N'$.DeliveryMonth',
                OrderQty FLOAT N'$.OrderQty', GroupQty FLOAT N'$.GroupQty', Verdict NVARCHAR(10) N'$.Verdict', VerdictCode NCHAR(1) N'$.VerdictCode',
                MaxQty FLOAT N'$.MaxQty', ReasonCode NVARCHAR(40) N'$.ReasonCode', RuleId NVARCHAR(30) N'$.RuleId',
                ReasonText NVARCHAR(400) N'$.ReasonText', Engine NVARCHAR(10) N'$.Engine', EngineDecision NVARCHAR(100) N'$.EngineDecision',
                EngineReason NVARCHAR(MAX) N'$.EngineReason', MetricsJson NVARCHAR(MAX) N'$.MetricsJson', WorkingJson NVARCHAR(MAX) N'$.WorkingJson') AS j
         ORDER BY CAST(a.[key] AS INT);
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        IF ERROR_NUMBER() IN (2601, 2627)
            SELECT @Error_Code = 50409, @Error_Message = N'This request_id has already been decided.';
        ELSE
            SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* @Cmd BY_REQUEST -> the answered (HTTP 200) decision of @Request_Id, with Body_Hash and Response_Json (idempotency)
        BY_ID      -> the row @Id with Request_Json / Response_Json, then its items, then its documents (MRN, current first)
        BY_VIEW_TOKEN -> the same three result sets for the decision of @View_Token (verdict link; expiry checked by the API)
        REPORT     -> answered decisions with their current document (MRN), newest first, max @Max rows; filters @From / @To
                      (business day of Decided_At), @Po_Number (contains: PO, request_id or document no), @Verdict, @Mode,
                      @Has_Document ('yes' / 'no' / all), @Include_Tests (0 = SAP calls only); @Total OUTPUT
        LIST       -> newest first, max @Max rows; filters @From / @To (dates, business day of Decided_At in @Tz_Offset_Min),
                      @Po_Number (contains), @Verdict, @Mode, @Status ('ok' = 200, 'error' = not 200); @Total OUTPUT
        STATS      -> counts (today = @Today in the business time zone) and latency percentiles of the last 1000 answered
                      SAP calls (portal tests excluded) */
CREATE OR ALTER PROCEDURE dbo.PRO_SAP_DECISION_GET
    @Cmd             NVARCHAR(20),
    @Request_Id      NVARCHAR(80)  = NULL,
    @Id              BIGINT = NULL,
    @From            DATE = NULL,
    @To              DATE = NULL,
    @Po_Number       NVARCHAR(80)  = NULL,
    @Verdict         NVARCHAR(10)  = NULL,
    @Mode            NVARCHAR(10)  = NULL,
    @Status          NVARCHAR(10)  = NULL,
    @Max             INT = 200,
    @Today           DATE = NULL,
    @Tz_Offset_Min   INT = 330,
    @View_Token      NVARCHAR(64)  = NULL,
    @Has_Document    NVARCHAR(10)  = NULL,
    @Include_Tests   BIT = 0,
    @Total           INT = NULL OUTPUT,
    @Error_Code      INT = NULL OUTPUT,
    @Error_Message   NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        IF @Cmd = N'BY_REQUEST'
            SELECT TOP (1) Id, Decision_Id AS DecisionId, Request_Id AS RequestId, Body_Hash AS BodyHash, Mode, Http_Status AS HttpStatus,
                   Overall_Verdict AS OverallVerdict, Decided_At AS DecidedAt, Response_Json AS ResponseJson,
                   View_Token AS ViewToken, View_Expires_At AS ViewExpiresAt
              FROM dbo.SAP_DECISION_LOG WHERE Request_Id = @Request_Id AND Http_Status = 200;
        ELSE IF @Cmd IN (N'BY_ID', N'BY_VIEW_TOKEN')
        BEGIN
            IF @Cmd = N'BY_VIEW_TOKEN'
                SELECT @Id = Id FROM dbo.SAP_DECISION_LOG WHERE View_Token = @View_Token AND @View_Token IS NOT NULL AND @View_Token <> N'';
            SELECT Id, Decision_Id AS DecisionId, Request_Id AS RequestId, Body_Hash AS BodyHash, Mode, Source_System AS SourceSystem, Client,
                   Po_Number AS PoNumber, Buyer_User AS BuyerUser, Division, Overall_Verdict AS OverallVerdict, Verdict_Code AS VerdictCode,
                   Route_To AS RouteTo, Bm_Head_Required AS BmHeadRequired, Http_Status AS HttpStatus, Error_Code AS ErrorCode,
                   Error_Message AS ErrorMessage, Item_Count AS ItemCount, Data_Delayed AS DataDelayed, Events_Lag_Seconds AS EventsLagSeconds,
                   Rules_Version AS RulesVersion, Duration_Ms AS DurationMs, Tech_User AS TechUser, Client_Ip AS ClientIp, Is_Test AS IsTest,
                   Trace_Id AS TraceId, Requested_At AS RequestedAt, Decided_At AS DecidedAt, Request_Json AS RequestJson, Response_Json AS ResponseJson,
                   View_Token AS ViewToken, View_Expires_At AS ViewExpiresAt
              FROM dbo.SAP_DECISION_LOG WHERE Id = @Id;
            SELECT Decision_Log_Id AS DecisionLogId, Po_Item AS PoItem, Article, Generic_Article AS GenericArticle, Gm, Gm_Match AS GmMatch,
                   Colour_Sent AS ColourSent, Colour, Colour_Match AS ColourMatch, Delivery_Month AS DeliveryMonth, Order_Qty AS OrderQty,
                   Group_Qty AS GroupQty, Verdict, Verdict_Code AS VerdictCode, Max_Qty AS MaxQty, Reason_Code AS ReasonCode, Rule_Id AS RuleId,
                   Reason_Text AS ReasonText, Engine, Engine_Decision AS EngineDecision, Engine_Reason AS EngineReason, Metrics_Json AS MetricsJson
              FROM dbo.SAP_DECISION_ITEMS WHERE Decision_Log_Id = @Id ORDER BY Po_Item, Id;
            SELECT Id, Decision_Log_Id AS DecisionLogId, Request_Id AS RequestId, Decision_Id AS DecisionId, Document_No AS DocumentNo,
                   Document_Type AS DocumentType, Po_Number AS PoNumber, CONVERT(NVARCHAR(10), Document_Date, 23) AS DocumentDate, Note,
                   Is_Current AS IsCurrent, Replaced_At AS ReplacedAt, Tech_User AS TechUser, Source_System AS SourceSystem,
                   Received_At AS ReceivedAt
              FROM dbo.SAP_DECISION_DOCUMENTS WHERE Decision_Log_Id = @Id ORDER BY Is_Current DESC, Id DESC;
        END
        ELSE IF @Cmd = N'REPORT'
        BEGIN
            DECLARE @rep TABLE (Id BIGINT PRIMARY KEY, DocId BIGINT NULL);
            INSERT INTO @rep (Id, DocId)
            SELECT l.Id, d.Id
              FROM dbo.SAP_DECISION_LOG l
              LEFT JOIN dbo.SAP_DECISION_DOCUMENTS d ON d.Decision_Log_Id = l.Id AND d.Is_Current = 1
             WHERE l.Http_Status = 200
               AND (ISNULL(@Include_Tests, 0) = 1 OR l.Is_Test = 0)
               AND (@From IS NULL OR CAST(SWITCHOFFSET(l.Decided_At, @Tz_Offset_Min) AS DATE) >= @From)
               AND (@To IS NULL OR CAST(SWITCHOFFSET(l.Decided_At, @Tz_Offset_Min) AS DATE) <= @To)
               AND (@Po_Number IS NULL OR @Po_Number = N'' OR l.Po_Number LIKE N'%' + @Po_Number + N'%'
                    OR l.Request_Id LIKE N'%' + @Po_Number + N'%' OR d.Document_No LIKE N'%' + @Po_Number + N'%')
               AND (@Verdict IS NULL OR @Verdict = N'' OR l.Overall_Verdict = @Verdict)
               AND (@Mode IS NULL OR @Mode = N'' OR l.Mode = @Mode)
               AND (@Has_Document IS NULL OR @Has_Document IN (N'', N'all') OR (@Has_Document = N'yes' AND d.Id IS NOT NULL)
                    OR (@Has_Document = N'no' AND d.Id IS NULL));
            SET @Total = @@ROWCOUNT;
            SELECT TOP (ISNULL(@Max, 500)) l.Id, l.Decision_Id AS DecisionId, l.Request_Id AS RequestId, l.Po_Number AS PoNumber,
                   l.Buyer_User AS BuyerUser, l.Division, l.Mode, l.Overall_Verdict AS OverallVerdict, l.Route_To AS RouteTo,
                   l.Item_Count AS ItemCount,
                   (SELECT SUM(i.Order_Qty) FROM dbo.SAP_DECISION_ITEMS i WHERE i.Decision_Log_Id = l.Id) AS OrderQty,
                   l.Source_System AS SourceSystem, l.Is_Test AS IsTest, l.Decided_At AS DecidedAt,
                   d.Document_No AS DocumentNo, d.Document_Type AS DocumentType, CONVERT(NVARCHAR(10), d.Document_Date, 23) AS DocumentDate,
                   d.Received_At AS DocumentReceivedAt
              FROM @rep r
              JOIN dbo.SAP_DECISION_LOG l ON l.Id = r.Id
              LEFT JOIN dbo.SAP_DECISION_DOCUMENTS d ON d.Id = r.DocId
             ORDER BY l.Id DESC;
        END
        ELSE IF @Cmd = N'LIST'
        BEGIN
            DECLARE @rows TABLE (Id BIGINT PRIMARY KEY);
            INSERT INTO @rows (Id)
            SELECT Id FROM dbo.SAP_DECISION_LOG
             WHERE (@From IS NULL OR CAST(SWITCHOFFSET(Decided_At, @Tz_Offset_Min) AS DATE) >= @From)
               AND (@To IS NULL OR CAST(SWITCHOFFSET(Decided_At, @Tz_Offset_Min) AS DATE) <= @To)
               AND (@Po_Number IS NULL OR @Po_Number = N'' OR Po_Number LIKE N'%' + @Po_Number + N'%' OR Request_Id LIKE N'%' + @Po_Number + N'%')
               AND (@Verdict IS NULL OR @Verdict = N'' OR Overall_Verdict = @Verdict)
               AND (@Mode IS NULL OR @Mode = N'' OR Mode = @Mode)
               AND (@Status IS NULL OR @Status IN (N'', N'all') OR (@Status = N'ok' AND Http_Status = 200) OR (@Status = N'error' AND Http_Status <> 200));
            SET @Total = @@ROWCOUNT;
            SELECT TOP (ISNULL(@Max, 200)) l.Id, Decision_Id AS DecisionId, Request_Id AS RequestId, Mode, Source_System AS SourceSystem, Client,
                   Po_Number AS PoNumber, Buyer_User AS BuyerUser, Division, Overall_Verdict AS OverallVerdict, Verdict_Code AS VerdictCode,
                   Route_To AS RouteTo, Bm_Head_Required AS BmHeadRequired, Http_Status AS HttpStatus, Error_Code AS ErrorCode,
                   Error_Message AS ErrorMessage, Item_Count AS ItemCount, Data_Delayed AS DataDelayed, Events_Lag_Seconds AS EventsLagSeconds,
                   Rules_Version AS RulesVersion, Duration_Ms AS DurationMs, Tech_User AS TechUser, Client_Ip AS ClientIp, Is_Test AS IsTest,
                   Trace_Id AS TraceId, Requested_At AS RequestedAt, Decided_At AS DecidedAt
              FROM dbo.SAP_DECISION_LOG l JOIN @rows r ON r.Id = l.Id
             ORDER BY l.Id DESC;
        END
        ELSE IF @Cmd = N'STATS'
        BEGIN
            DECLARE @p50 FLOAT, @p95 FLOAT, @mx INT;
            ;WITH lastCalls AS (
                SELECT TOP (1000) Duration_Ms FROM dbo.SAP_DECISION_LOG WHERE Is_Test = 0 AND Http_Status = 200 ORDER BY Id DESC)
            SELECT TOP (1) @p50 = PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY Duration_Ms) OVER (),
                           @p95 = PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY Duration_Ms) OVER (),
                           @mx = MAX(Duration_Ms) OVER ()
              FROM lastCalls;
            SELECT SUM(CASE WHEN CAST(SWITCHOFFSET(Decided_At, @Tz_Offset_Min) AS DATE) = @Today THEN 1 ELSE 0 END) AS Today,
                   SUM(CASE WHEN CAST(SWITCHOFFSET(Decided_At, @Tz_Offset_Min) AS DATE) = @Today AND Http_Status <> 200 THEN 1 ELSE 0 END) AS TodayNotChecked,
                   COUNT(*) AS Total,
                   SUM(CASE WHEN Http_Status <> 200 THEN 1 ELSE 0 END) AS NotChecked,
                   SUM(CASE WHEN Overall_Verdict = N'ALLOW' THEN 1 ELSE 0 END) AS Allow,
                   SUM(CASE WHEN Overall_Verdict = N'REDUCE' THEN 1 ELSE 0 END) AS Reduce,
                   SUM(CASE WHEN Overall_Verdict = N'WATCH' THEN 1 ELSE 0 END) AS Watch,
                   SUM(CASE WHEN Overall_Verdict = N'HOLD' THEN 1 ELSE 0 END) AS Hold,
                   SUM(CASE WHEN Overall_Verdict = N'BLOCK' THEN 1 ELSE 0 END) AS Block,
                   SUM(CASE WHEN Mode = N'SHADOW' AND Http_Status = 200 THEN 1 ELSE 0 END) AS Shadow,
                   SUM(CASE WHEN Mode = N'LIVE' AND Http_Status = 200 THEN 1 ELSE 0 END) AS Live,
                   CAST(ROUND(@p50, 0) AS INT) AS P50Ms, CAST(ROUND(@p95, 0) AS INT) AS P95Ms, @mx AS MaxMs
              FROM dbo.SAP_DECISION_LOG WHERE Is_Test = 0;
        END
        ELSE
            THROW 50001, N'PRO_SAP_DECISION_GET: unknown @Cmd.', 1;
    END TRY
    BEGIN CATCH
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* POST /documents: the final SAP document (MRN) of an answered request_id, in one transaction.
   @Result OUTPUT: STORED (first document) | UNCHANGED (same Document_No again: idempotent) | REPLACED (@Replace = 1, the
   old row is kept with Is_Current = 0). @Error_Code 50404 no answered decision for @Request_Id; 50409 another
   Document_No is already stored (@Current_Document_No OUTPUT) and @Replace = 0. */
CREATE OR ALTER PROCEDURE dbo.PRO_SAP_DOCUMENT_SAVE
    @Request_Id          NVARCHAR(80),
    @Document_No         NVARCHAR(40),
    @Document_Type       NVARCHAR(20)  = NULL,
    @Po_Number           NVARCHAR(20)  = NULL,
    @Document_Date       DATE          = NULL,
    @Note                NVARCHAR(500) = NULL,
    @Replace             BIT = 0,
    @Tech_User           NVARCHAR(200) = NULL,
    @Source_System       NVARCHAR(10)  = NULL,
    @Client_Ip           NVARCHAR(64)  = NULL,
    @Trace_Id            NVARCHAR(20)  = NULL,
    @Request_Json        NVARCHAR(MAX) = NULL,
    @Result              NVARCHAR(20)  = NULL OUTPUT,
    @Decision_Id         NVARCHAR(30)  = NULL OUTPUT,
    @Current_Document_No NVARCHAR(40)  = NULL OUTPUT,
    @Received_At         DATETIMEOFFSET(7) = NULL OUTPUT,
    @Error_Code          INT = NULL OUTPUT,
    @Error_Message       NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = N'', @Result = NULL;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @logId BIGINT, @logPo NVARCHAR(20);
        SELECT @logId = Id, @Decision_Id = Decision_Id, @logPo = Po_Number
          FROM dbo.SAP_DECISION_LOG WITH (UPDLOCK, HOLDLOCK)
         WHERE Request_Id = @Request_Id AND Http_Status = 200;
        IF @logId IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT @Error_Code = 50404, @Error_Message = N'No answered decision for request_id ' + ISNULL(@Request_Id, N'(null)');
            RETURN;
        END

        DECLARE @curId BIGINT, @curReceived DATETIMEOFFSET(7);
        SELECT @curId = Id, @Current_Document_No = Document_No, @curReceived = Received_At
          FROM dbo.SAP_DECISION_DOCUMENTS WITH (UPDLOCK, HOLDLOCK)
         WHERE Request_Id = @Request_Id AND Is_Current = 1;

        IF @curId IS NOT NULL AND @Current_Document_No = @Document_No
        BEGIN
            COMMIT TRANSACTION;
            SELECT @Result = N'UNCHANGED', @Received_At = @curReceived;
            RETURN;
        END
        IF @curId IS NOT NULL AND ISNULL(@Replace, 0) = 0
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT @Error_Code = 50409,
                   @Error_Message = N'request_id ' + @Request_Id + N' already has document ' + @Current_Document_No + N' - send replace = true to correct it';
            RETURN;
        END

        SET @Received_At = SYSDATETIMEOFFSET();
        IF @curId IS NOT NULL
            UPDATE dbo.SAP_DECISION_DOCUMENTS SET Is_Current = 0, Replaced_At = @Received_At WHERE Id = @curId;
        INSERT INTO dbo.SAP_DECISION_DOCUMENTS (Decision_Log_Id, Request_Id, Decision_Id, Document_No, Document_Type, Po_Number, Document_Date,
                    Note, Is_Current, Tech_User, Source_System, Client_Ip, Trace_Id, Received_At, Request_Json)
        VALUES (@logId, @Request_Id, @Decision_Id, @Document_No, ISNULL(NULLIF(@Document_Type, N''), N'MRN'), ISNULL(NULLIF(@Po_Number, N''), @logPo),
                @Document_Date, @Note, 1, @Tech_User, @Source_System, @Client_Ip, @Trace_Id, @Received_At, @Request_Json);
        COMMIT TRANSACTION;
        SET @Result = CASE WHEN @curId IS NULL THEN N'STORED' ELSE N'REPLACED' END;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        IF ERROR_NUMBER() IN (2601, 2627)
            SELECT @Error_Code = 50409, @Error_Message = N'A document for request_id ' + ISNULL(@Request_Id, N'') + N' was stored at the same time - send it again';
        ELSE
            SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* ───────────────────────────── events ───────────────────────────── */

/* Stores one /events batch in ONE transaction (the API answers 200 only after the commit, spec §5 delivery contract).
   @Events = JSON array in batch order: {MsgId, Seq, EventType, OccurredAt, SapUser, PoNumber, PoItem, MaterialDoc,
   DecisionId, Status (ACCEPTED | PARKED), ErrorCode, ErrorMessage, PayloadJson}. Per event, in order:
     msg_id already stored (or earlier in this batch) -> DUPLICATE: nothing inserted, Dup_Count + 1 on the stored row;
     otherwise inserted with Seq_Status = LATE when an accepted event's seq is not above the last accepted seq of @Queue
     (IN_ORDER otherwise), Applied = 0.
   Returns one row per event: (MsgId, Result = ACCEPTED | PARKED | DUPLICATE), in batch order. */
CREATE OR ALTER PROCEDURE dbo.PRO_SAP_EVENTS_SAVE
    @Events          NVARCHAR(MAX),
    @Queue           NVARCHAR(64)  = NULL,
    @Source_System   NVARCHAR(10)  = NULL,
    @Client          NVARCHAR(10)  = NULL,
    @Tech_User       NVARCHAR(64)  = NULL,
    @Batch_Id        NVARCHAR(40)  = NULL,
    @Error_Code      INT = NULL OUTPUT,
    @Error_Message   NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        DECLARE @in TABLE (Ord INT PRIMARY KEY, Msg_Id NVARCHAR(100) COLLATE Latin1_General_100_BIN2, Seq BIGINT, Event_Type NVARCHAR(30),
                           Occurred_At NVARCHAR(40), Sap_User NVARCHAR(40), Po_Number NVARCHAR(20), Po_Item INT, Material_Doc NVARCHAR(20),
                           Decision_Id NVARCHAR(30), Status NVARCHAR(10), Error_Code NVARCHAR(50), Error_Message NVARCHAR(500), Payload_Json NVARCHAR(MAX));
        INSERT INTO @in
        SELECT CAST(a.[key] AS INT), j.MsgId, j.Seq, j.EventType, j.OccurredAt, LEFT(j.SapUser, 40), LEFT(j.PoNumber, 20), j.PoItem,
               LEFT(j.MaterialDoc, 20), LEFT(j.DecisionId, 30), j.Status, j.ErrorCode, LEFT(j.ErrorMessage, 500), j.PayloadJson
          FROM OPENJSON(@Events) AS a
         CROSS APPLY OPENJSON(a.value) WITH (
                MsgId NVARCHAR(100) N'$.MsgId', Seq BIGINT N'$.Seq', EventType NVARCHAR(30) N'$.EventType', OccurredAt NVARCHAR(40) N'$.OccurredAt',
                SapUser NVARCHAR(200) N'$.SapUser', PoNumber NVARCHAR(200) N'$.PoNumber', PoItem INT N'$.PoItem', MaterialDoc NVARCHAR(200) N'$.MaterialDoc',
                DecisionId NVARCHAR(200) N'$.DecisionId', Status NVARCHAR(10) N'$.Status', ErrorCode NVARCHAR(50) N'$.ErrorCode',
                ErrorMessage NVARCHAR(MAX) N'$.ErrorMessage', PayloadJson NVARCHAR(MAX) N'$.PayloadJson') AS j;

        DECLARE @out TABLE (Ord INT PRIMARY KEY, Msg_Id NVARCHAR(100), Result NVARCHAR(10));
        DECLARE @now DATETIMEOFFSET(7) = SYSDATETIMEOFFSET();

        BEGIN TRANSACTION;
        DECLARE @lastSeq BIGINT = (SELECT MAX(Seq) FROM dbo.SAP_EVENTS WITH (UPDLOCK, HOLDLOCK)
                                    WHERE ISNULL(Queue, N'') = ISNULL(@Queue, N'') AND Status = N'ACCEPTED');
        DECLARE @ord INT, @msg NVARCHAR(100), @seq BIGINT, @status NVARCHAR(10);
        DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Ord, Msg_Id, Seq, Status FROM @in ORDER BY Ord;
        OPEN c;
        FETCH NEXT FROM c INTO @ord, @msg, @seq, @status;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.SAP_EVENTS WITH (UPDLOCK, HOLDLOCK) WHERE Msg_Id = @msg)
            BEGIN
                UPDATE dbo.SAP_EVENTS SET Dup_Count = Dup_Count + 1, Last_Dup_At = @now WHERE Msg_Id = @msg;
                INSERT INTO @out VALUES (@ord, @msg, N'DUPLICATE');
            END
            ELSE
            BEGIN
                INSERT INTO dbo.SAP_EVENTS (Msg_Id, Seq, Queue, Source_System, Client, Event_Type, Occurred_At, Received_At, Sap_User, Po_Number,
                            Po_Item, Material_Doc, Decision_Id, Status, Error_Code, Error_Message, Seq_Status, Applied, Tech_User, Batch_Id, Payload_Json)
                SELECT i.Msg_Id, i.Seq, @Queue, @Source_System, @Client, i.Event_Type, TRY_CONVERT(DATETIMEOFFSET(7), i.Occurred_At), @now,
                       i.Sap_User, i.Po_Number, i.Po_Item, i.Material_Doc, i.Decision_Id, i.Status, i.Error_Code, i.Error_Message,
                       CASE WHEN i.Status = N'ACCEPTED' AND @lastSeq IS NOT NULL AND i.Seq <= @lastSeq THEN N'LATE' ELSE N'IN_ORDER' END,
                       0, @Tech_User, @Batch_Id, ISNULL(i.Payload_Json, N'{}')
                  FROM @in i WHERE i.Ord = @ord;
                IF @status = N'ACCEPTED' AND @seq IS NOT NULL AND (@lastSeq IS NULL OR @seq > @lastSeq) SET @lastSeq = @seq;
                INSERT INTO @out VALUES (@ord, @msg, @status);
            END
            FETCH NEXT FROM c INTO @ord, @msg, @seq, @status;
        END
        CLOSE c;
        DEALLOCATE c;
        COMMIT TRANSACTION;

        SELECT Msg_Id AS MsgId, Result FROM @out ORDER BY Ord;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* @Cmd STATUS -> one row: last received / occurred, last seq, lag of the most recently received accepted event (seconds),
                  counts (total, accepted, parked, late, not applied, received today = @Today in the business time zone)
        LIST   -> newest first, max @Max; filters @From / @To (business day of Received_At), @Event_Type, @Status
                  (ACCEPTED / PARKED / LATE / DUPLICATED), @Q (msg id, PO, material doc, decision id contains); @Total OUTPUT
        BY_ID  -> one row with Payload_Json */
CREATE OR ALTER PROCEDURE dbo.PRO_SAP_EVENTS_GET
    @Cmd             NVARCHAR(20),
    @Id              BIGINT = NULL,
    @From            DATE = NULL,
    @To              DATE = NULL,
    @Event_Type      NVARCHAR(30)  = NULL,
    @Status          NVARCHAR(20)  = NULL,
    @Q               NVARCHAR(100) = NULL,
    @Max             INT = 200,
    @Today           DATE = NULL,
    @Tz_Offset_Min   INT = 330,
    @Total           INT = NULL OUTPUT,
    @Error_Code      INT = NULL OUTPUT,
    @Error_Message   NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        IF @Cmd = N'STATUS'
        BEGIN
            DECLARE @lastRecv DATETIMEOFFSET(7), @lastOcc DATETIMEOFFSET(7), @lag BIGINT;
            SELECT TOP (1) @lastRecv = Received_At, @lastOcc = Occurred_At,
                   @lag = CASE WHEN Occurred_At IS NULL THEN 0 ELSE DATEDIFF_BIG(SECOND, Occurred_At, Received_At) END
              FROM dbo.SAP_EVENTS WHERE Status = N'ACCEPTED' ORDER BY Id DESC;
            SELECT @lastRecv AS LastReceivedAt, @lastOcc AS LastOccurredAt,
                   (SELECT MAX(Seq) FROM dbo.SAP_EVENTS WHERE Status = N'ACCEPTED') AS LastSeq,
                   CASE WHEN ISNULL(@lag, 0) < 0 THEN 0 ELSE ISNULL(@lag, 0) END AS LagSeconds,
                   COUNT(*) AS Total,
                   ISNULL(SUM(CASE WHEN Status = N'ACCEPTED' THEN 1 ELSE 0 END), 0) AS Accepted,
                   ISNULL(SUM(CASE WHEN Status = N'PARKED' THEN 1 ELSE 0 END), 0) AS Parked,
                   ISNULL(SUM(CASE WHEN Seq_Status = N'LATE' THEN 1 ELSE 0 END), 0) AS Late,
                   ISNULL(SUM(CASE WHEN Status = N'ACCEPTED' AND Applied = 0 THEN 1 ELSE 0 END), 0) AS NotApplied,
                   ISNULL(SUM(CASE WHEN CAST(SWITCHOFFSET(Received_At, @Tz_Offset_Min) AS DATE) = @Today THEN 1 ELSE 0 END), 0) AS Today
              FROM dbo.SAP_EVENTS;
        END
        ELSE IF @Cmd = N'LIST'
        BEGIN
            DECLARE @rows TABLE (Id BIGINT PRIMARY KEY);
            INSERT INTO @rows (Id)
            SELECT Id FROM dbo.SAP_EVENTS
             WHERE (@From IS NULL OR CAST(SWITCHOFFSET(Received_At, @Tz_Offset_Min) AS DATE) >= @From)
               AND (@To IS NULL OR CAST(SWITCHOFFSET(Received_At, @Tz_Offset_Min) AS DATE) <= @To)
               AND (@Event_Type IS NULL OR @Event_Type = N'' OR Event_Type = @Event_Type)
               AND (@Status IS NULL OR @Status IN (N'', N'all') OR Status = @Status OR (@Status = N'LATE' AND Seq_Status = N'LATE')
                    OR (@Status = N'DUPLICATED' AND Dup_Count > 0))
               AND (@Q IS NULL OR @Q = N'' OR Msg_Id LIKE N'%' + @Q + N'%' OR Po_Number LIKE N'%' + @Q + N'%'
                    OR Material_Doc LIKE N'%' + @Q + N'%' OR Decision_Id LIKE N'%' + @Q + N'%');
            SET @Total = @@ROWCOUNT;
            SELECT TOP (ISNULL(@Max, 200)) e.Id, Msg_Id AS MsgId, Seq, Queue, Source_System AS SourceSystem, Client, Event_Type AS EventType,
                   Occurred_At AS OccurredAt, Received_At AS ReceivedAt, Sap_User AS SapUser, Po_Number AS PoNumber, Po_Item AS PoItem,
                   Material_Doc AS MaterialDoc, Decision_Id AS DecisionId, Status, Error_Code AS ErrorCode, Error_Message AS ErrorMessage,
                   Seq_Status AS SeqStatus, Applied, Applied_At AS AppliedAt, Dup_Count AS DupCount, Last_Dup_At AS LastDupAt,
                   CASE WHEN Occurred_At IS NULL THEN 0 ELSE DATEDIFF_BIG(SECOND, Occurred_At, Received_At) END AS LagSeconds,
                   Tech_User AS TechUser, Batch_Id AS BatchId
              FROM dbo.SAP_EVENTS e JOIN @rows r ON r.Id = e.Id
             ORDER BY e.Id DESC;
        END
        ELSE IF @Cmd = N'BY_ID'
            SELECT Id, Msg_Id AS MsgId, Seq, Queue, Source_System AS SourceSystem, Client, Event_Type AS EventType, Occurred_At AS OccurredAt,
                   Received_At AS ReceivedAt, Sap_User AS SapUser, Po_Number AS PoNumber, Po_Item AS PoItem, Material_Doc AS MaterialDoc,
                   Decision_Id AS DecisionId, Status, Error_Code AS ErrorCode, Error_Message AS ErrorMessage, Seq_Status AS SeqStatus, Applied,
                   Applied_At AS AppliedAt, Dup_Count AS DupCount, Last_Dup_At AS LastDupAt,
                   CASE WHEN Occurred_At IS NULL THEN 0 ELSE DATEDIFF_BIG(SECOND, Occurred_At, Received_At) END AS LagSeconds,
                   Tech_User AS TechUser, Batch_Id AS BatchId, Payload_Json AS PayloadJson
              FROM dbo.SAP_EVENTS WHERE Id = @Id;
        ELSE
            THROW 50001, N'PRO_SAP_EVENTS_GET: unknown @Cmd.', 1;
    END TRY
    BEGIN CATCH
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* ───────────────────────────── reason codes ───────────────────────────── */

/* @Cmd LIST   -> rows of @Code_Type (NULL = both), ordered by type, Sort_Order, Code
        ACTIVE -> active EXCEPTION codes (GET /reason-codes), with @Version OUTPUT = date of the last change (yyyy-MM-dd)
        SAVE   -> insert or update an EXCEPTION code (@Code, @Text, @Active, @Sort_Order, @By); VERDICT codes come from the seed */
CREATE OR ALTER PROCEDURE dbo.PRO_SAP_REASON_CODES
    @Cmd             NVARCHAR(20),
    @Code_Type       NVARCHAR(10)  = NULL,
    @Code            NVARCHAR(40)  = NULL,
    @Text            NVARCHAR(200) = NULL,
    @Active          BIT = 1,
    @Sort_Order      INT = NULL,
    @By              NVARCHAR(200) = NULL,
    @Tz_Offset_Min   INT = 330,
    @Version         NVARCHAR(20)  = NULL OUTPUT,
    @Error_Code      INT = NULL OUTPUT,
    @Error_Message   NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        IF @Cmd = N'LIST'
            SELECT Code_Type AS CodeType, Code, Text, Active, Sort_Order AS SortOrder, Verdict, Rule_Ids AS RuleIds,
                   Updated_At AS UpdatedAt, Updated_By AS UpdatedBy
              FROM dbo.SAP_REASON_CODES
             WHERE @Code_Type IS NULL OR Code_Type = @Code_Type
             ORDER BY Code_Type, Sort_Order, Code;
        ELSE IF @Cmd = N'ACTIVE'
        BEGIN
            SELECT @Version = CONVERT(NVARCHAR(10), CAST(SWITCHOFFSET(MAX(Updated_At), @Tz_Offset_Min) AS DATE), 23)
              FROM dbo.SAP_REASON_CODES WHERE Code_Type = N'EXCEPTION';
            SELECT Code_Type AS CodeType, Code, Text, Active, Sort_Order AS SortOrder
              FROM dbo.SAP_REASON_CODES WHERE Code_Type = N'EXCEPTION' AND Active = 1
             ORDER BY Sort_Order, Code;
        END
        ELSE IF @Cmd = N'SAVE'
        BEGIN
            SET @Code = UPPER(LTRIM(RTRIM(@Code)));
            UPDATE dbo.SAP_REASON_CODES
               SET Text = @Text, Active = ISNULL(@Active, 1), Sort_Order = ISNULL(@Sort_Order, Sort_Order),
                   Updated_At = SYSDATETIMEOFFSET(), Updated_By = @By
             WHERE Code_Type = N'EXCEPTION' AND Code = @Code;
            IF @@ROWCOUNT = 0
                INSERT INTO dbo.SAP_REASON_CODES (Code_Type, Code, Text, Active, Sort_Order, Updated_By)
                VALUES (N'EXCEPTION', @Code, @Text, ISNULL(@Active, 1),
                        ISNULL(@Sort_Order, (SELECT ISNULL(MAX(Sort_Order), 0) + 10 FROM dbo.SAP_REASON_CODES WHERE Code_Type = N'EXCEPTION')), @By);
            SELECT Code_Type AS CodeType, Code, Text, Active, Sort_Order AS SortOrder, Verdict, Rule_Ids AS RuleIds,
                   Updated_At AS UpdatedAt, Updated_By AS UpdatedBy
              FROM dbo.SAP_REASON_CODES WHERE Code_Type = N'EXCEPTION' AND Code = @Code;
        END
        ELSE
            THROW 50001, N'PRO_SAP_REASON_CODES: unknown @Cmd.', 1;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* ───────────────────────────── SAP MRAs (v6, v7) ─────────────────────────────
   MRA = SAP Merchandise Request (the pre-PO document). v1.1.7 rules (agreed 4-5 Nov 2026):
   - each morning the planner uploads the MRA workbook in Data Setup (item lines + header report); it REPLACES the
     previous list (SAP_MRA_FILE, one row per MRA, all sites added up; SAP_MRA_FILE_UPLOADS one row per upload);
   - during the day SAP sends every MRA change (level 1..9 / X) and PO change to POST /api/sap/v1/mra-events and
     /po-events; every message is stored as received, also a bad one;
   - the tool replays file + messages of the current data version (DecisionEngine/Mra/MraFeed.cs) and decides what
     takes capacity (MraCapacity.cs). Nothing is worked out in SQL.
   v7 (6 Nov 2026) — SAP changes received before the day's file:
   - SAP_MRA_CONTROL (one row): IsDailyDataUpload = today's MRA file is uploaded (Data_Day = today), and the current
     Data_Version (= Upload_Id of the file in use). On a new day the flag counts as false until the file is uploaded;
   - a message received while IsDailyDataUpload is false goes to SAP_MRA_FEED_PENDING (temporary table) and is not
     applied yet; while it is true it goes to SAP_MRA_FEED with the current Data_Version (applied at once);
   - the upload moves the pending messages (arrival order) into SAP_MRA_FEED with the new Data_Version, re-tags the
     messages already applied today to it, empties SAP_MRA_FEED_PENDING and sets IsDailyDataUpload = 1. So a change
     SAP sent before the file — also for an MRA that is not in the file — is applied on top of the new file.
   v5 (MRA status SAP_MRA_STATUS / _HISTORY / _UPLOADS, PRO_SAP_MRA_STATUS) is replaced and dropped. */

IF OBJECT_ID(N'dbo.PRO_SAP_MRA_STATUS', N'P') IS NOT NULL DROP PROCEDURE dbo.PRO_SAP_MRA_STATUS;
IF OBJECT_ID(N'dbo.SAP_MRA_STATUS_HISTORY', N'U') IS NOT NULL DROP TABLE dbo.SAP_MRA_STATUS_HISTORY;
IF OBJECT_ID(N'dbo.SAP_MRA_STATUS', N'U') IS NOT NULL DROP TABLE dbo.SAP_MRA_STATUS;
IF OBJECT_ID(N'dbo.SAP_MRA_UPLOADS', N'U') IS NOT NULL DROP TABLE dbo.SAP_MRA_UPLOADS;
GO

IF OBJECT_ID(N'dbo.SAP_MRA_FILE', N'U') IS NULL
CREATE TABLE dbo.SAP_MRA_FILE (
    Mra_No               NVARCHAR(30)  NOT NULL CONSTRAINT PK_SAP_MRA_FILE PRIMARY KEY,   -- without leading zeros
    Generic_Article      NVARCHAR(10)  NOT NULL,                                          -- last 10 digits
    Colour               NVARCHAR(40)  NOT NULL,
    Qty                  BIGINT        NOT NULL,                                          -- all item lines / sites added up
    Delivery_Date        DATE          NOT NULL,
    Mra_Level            NVARCHAR(2)   NOT NULL,                                          -- 1..9 / X
    Pos_Json             NVARCHAR(MAX) NULL,                                              -- [{Po, Status, PoDate}] from the header sheet
    Extra_Json           NVARCHAR(MAX) NULL,                                              -- created by / on, inspiration, description, lines, decision sent
    Upload_Id            NVARCHAR(40)  NOT NULL
);
GO
IF OBJECT_ID(N'dbo.SAP_MRA_FILE_UPLOADS', N'U') IS NULL
CREATE TABLE dbo.SAP_MRA_FILE_UPLOADS (
    Id                   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SAP_MRA_FILE_UPLOADS PRIMARY KEY,
    Upload_Id            NVARCHAR(40)  NOT NULL,                                          -- = the Data_Version it starts
    Upload_Day           DATE          NOT NULL,
    Uploaded_At          DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_SAP_MRA_FILE_UPLOADS_At DEFAULT SYSDATETIMEOFFSET(),
    Uploaded_By          NVARCHAR(200) NULL,
    File_Name            NVARCHAR(260) NULL,
    Mra_Count            INT           NOT NULL,
    Counts_Json          NVARCHAR(MAX) NULL                                               -- per level, rows read, set aside …
);
GO
IF COL_LENGTH(N'dbo.SAP_MRA_FILE_UPLOADS', N'Moved_From_Pending') IS NULL
    ALTER TABLE dbo.SAP_MRA_FILE_UPLOADS ADD Moved_From_Pending INT NULL;               -- v7: SAP changes moved in by this upload
GO
IF OBJECT_ID(N'dbo.SAP_MRA_FEED', N'U') IS NULL
CREATE TABLE dbo.SAP_MRA_FEED (
    Seq                  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SAP_MRA_FEED PRIMARY KEY,   -- apply order
    Received_At          DATETIMEOFFSET(7) NOT NULL,
    Received_Day         DATE          NOT NULL,
    Kind                 NVARCHAR(3)   NOT NULL,                                          -- mra / po
    By_User              NVARCHAR(200) NULL,                                              -- technical user or admin (test sender)
    Source_System        NVARCHAR(10)  NULL,
    Trace_Id             NVARCHAR(20)  NULL,
    Raw_Json             NVARCHAR(MAX) NOT NULL,                                          -- the message as received
    Msg_Json             NVARCHAR(MAX) NULL,                                              -- normalised message (null when not understood)
    Error                NVARCHAR(500) NULL                                               -- why it was set aside
);
GO
IF COL_LENGTH(N'dbo.SAP_MRA_FEED', N'Data_Version') IS NULL
    ALTER TABLE dbo.SAP_MRA_FEED ADD Data_Version NVARCHAR(40) NULL,                      -- v7: the file (Upload_Id) it applies on
                                     Pending_Seq  BIGINT NULL;                            -- v7: its number in the pending table, when moved
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SAP_MRA_FEED_Day' AND object_id = OBJECT_ID(N'dbo.SAP_MRA_FEED'))
    CREATE INDEX IX_SAP_MRA_FEED_Day ON dbo.SAP_MRA_FEED (Received_Day, Seq);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SAP_MRA_FEED_Version' AND object_id = OBJECT_ID(N'dbo.SAP_MRA_FEED'))
    CREATE INDEX IX_SAP_MRA_FEED_Version ON dbo.SAP_MRA_FEED (Data_Version, Seq);
GO
IF OBJECT_ID(N'dbo.SAP_MRA_FEED_PENDING', N'U') IS NULL
CREATE TABLE dbo.SAP_MRA_FEED_PENDING (
    Pending_Seq          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SAP_MRA_FEED_PENDING PRIMARY KEY,   -- arrival order
    Received_At          DATETIMEOFFSET(7) NOT NULL,
    Received_Day         DATE          NOT NULL,
    Kind                 NVARCHAR(3)   NOT NULL,
    By_User              NVARCHAR(200) NULL,
    Source_System        NVARCHAR(10)  NULL,
    Trace_Id             NVARCHAR(20)  NULL,
    Raw_Json             NVARCHAR(MAX) NOT NULL,
    Msg_Json             NVARCHAR(MAX) NULL,
    Error                NVARCHAR(500) NULL
);
GO
IF OBJECT_ID(N'dbo.SAP_MRA_CONTROL', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SAP_MRA_CONTROL (
        Id                    TINYINT       NOT NULL CONSTRAINT PK_SAP_MRA_CONTROL PRIMARY KEY CONSTRAINT CK_SAP_MRA_CONTROL_One CHECK (Id = 1),
        Data_Day              DATE          NULL,                                          -- day of the file in use
        Is_Daily_Data_Upload  BIT           NOT NULL CONSTRAINT DF_SAP_MRA_CONTROL_Flag DEFAULT (0),
        Data_Version          NVARCHAR(40)  NULL,                                          -- Upload_Id of the file in use
        Updated_At            DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_SAP_MRA_CONTROL_At DEFAULT SYSDATETIMEOFFSET()
    );
    -- from v6: the last upload becomes the data version; the messages replayed on it (received on or after its day) get it
    DECLARE @v NVARCHAR(40), @d DATE;
    SELECT TOP 1 @v = Upload_Id, @d = Upload_Day FROM dbo.SAP_MRA_FILE_UPLOADS ORDER BY Id DESC;
    INSERT INTO dbo.SAP_MRA_CONTROL (Id, Data_Day, Is_Daily_Data_Upload, Data_Version) VALUES (1, @d, CASE WHEN @v IS NULL THEN 0 ELSE 1 END, @v);
    IF @v IS NOT NULL UPDATE dbo.SAP_MRA_FEED SET Data_Version = @v WHERE Data_Version IS NULL AND Received_Day >= @d;
END
GO

/* SAP MRAs.
   @Cmd UPLOAD    morning file: SAP_MRA_FILE emptied and filled from @Rows (JSON array: MraNo, Ga, Colour, Qty, Dd,
                  Level, PosJson, ExtraJson); one SAP_MRA_FILE_UPLOADS row; the pending SAP changes are moved into
                  SAP_MRA_FEED with Data_Version = @Upload_Id (arrival order) and the changes already applied on @Upload_Day
                  are re-tagged to it; SAP_MRA_FEED_PENDING emptied; SAP_MRA_CONTROL: Data_Day = @Upload_Day,
                  IsDailyDataUpload = 1, Data_Version = @Upload_Id. ONE transaction (a failing file changes nothing).
                  OUTPUT @Moved_Count.
   @Cmd STATE     1st result: control (DataDay, IsDailyDataUpload for @Today, DataVersion); 2nd: the last upload; 3rd: the
                  file rows; 4th: the messages of the current data version (Seq order); 5th: the pending messages.
   @Cmd STAMP     (Data_Version, Is_Daily_Data_Upload for @Today, Max_Seq, Max_Pending_Seq): "has anything changed".
   @Cmd FEED_ADD  @Rows JSON array (Kind, RawJson, MsgJson, Error) stored in order: in SAP_MRA_FEED (current Data_Version)
                  when today's file is uploaded, else in SAP_MRA_FEED_PENDING. Returns (Idx, Seq, Pending).
   @Cmd DECISIONS (v8) the tool's answered SAP decision items since @Since (no tests): key (Gm, Colour, Delivery_Month),
                  decision, reason, Max Permissible, PO, request / decision ID, time, verdict link — linked to the MRAs in the API.
   @Cmd WORKING   (v8) Working_Json of decision item @Item_Id. */
CREATE OR ALTER PROCEDURE dbo.PRO_SAP_MRA
    @Cmd             NVARCHAR(20),
    @Rows            NVARCHAR(MAX) = NULL,
    @Upload_Id       NVARCHAR(40)  = NULL,
    @Upload_Day      DATE          = NULL,
    @File_Name       NVARCHAR(260) = NULL,
    @Counts_Json     NVARCHAR(MAX) = NULL,
    @By              NVARCHAR(200) = NULL,
    @Source_System   NVARCHAR(10)  = NULL,
    @Trace_Id        NVARCHAR(20)  = NULL,
    @Received_At     DATETIMEOFFSET(7) = NULL,
    @Today           DATE          = NULL,
    @Since           DATE          = NULL,
    @Item_Id         BIGINT        = NULL,
    @Moved_Count     INT = NULL OUTPUT,
    @Error_Code      INT = NULL OUTPUT,
    @Error_Message   NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        DECLARE @t DATE = ISNULL(@Today, CAST(SYSDATETIMEOFFSET() AS DATE));
        IF NOT EXISTS (SELECT 1 FROM dbo.SAP_MRA_CONTROL WHERE Id = 1)
            INSERT INTO dbo.SAP_MRA_CONTROL (Id, Is_Daily_Data_Upload) VALUES (1, 0);

        IF @Cmd = N'UPLOAD'
        BEGIN
            BEGIN TRANSACTION;
            DECLARE @prevVersion NVARCHAR(40), @prevDay DATE;
            -- the control row is the lock: SAP changes wait while the file is replaced
            SELECT @prevVersion = Data_Version, @prevDay = Data_Day FROM dbo.SAP_MRA_CONTROL WITH (UPDLOCK, HOLDLOCK) WHERE Id = 1;
            DELETE FROM dbo.SAP_MRA_FILE WITH (TABLOCKX);
            INSERT INTO dbo.SAP_MRA_FILE (Mra_No, Generic_Article, Colour, Qty, Delivery_Date, Mra_Level, Pos_Json, Extra_Json, Upload_Id)
            SELECT j.MraNo, j.Ga, j.Colour, j.Qty, j.Dd, j.Lvl, j.PosJson, j.ExtraJson, @Upload_Id
              FROM OPENJSON(@Rows) WITH (
                   MraNo NVARCHAR(30) '$.MraNo', Ga NVARCHAR(10) '$.Ga', Colour NVARCHAR(40) '$.Colour', Qty BIGINT '$.Qty',
                   Dd DATE '$.Dd', Lvl NVARCHAR(2) '$.Level', PosJson NVARCHAR(MAX) '$.PosJson', ExtraJson NVARCHAR(MAX) '$.ExtraJson') j;
            DECLARE @n INT = @@ROWCOUNT;
            -- changes already applied today (an earlier file of the same day) stay applied on the new file
            UPDATE dbo.SAP_MRA_FEED SET Data_Version = @Upload_Id
             WHERE @prevVersion IS NOT NULL AND Data_Version = @prevVersion AND Received_Day >= @Upload_Day;
            -- the changes SAP sent before the file: applied on top of it, in arrival order
            INSERT INTO dbo.SAP_MRA_FEED (Received_At, Received_Day, Kind, By_User, Source_System, Trace_Id, Raw_Json, Msg_Json, Error, Data_Version, Pending_Seq)
            SELECT Received_At, Received_Day, Kind, By_User, Source_System, Trace_Id, Raw_Json, Msg_Json, Error, @Upload_Id, Pending_Seq
              FROM dbo.SAP_MRA_FEED_PENDING WITH (TABLOCKX) ORDER BY Pending_Seq;
            SET @Moved_Count = @@ROWCOUNT;
            DELETE FROM dbo.SAP_MRA_FEED_PENDING;
            INSERT INTO dbo.SAP_MRA_FILE_UPLOADS (Upload_Id, Upload_Day, Uploaded_By, File_Name, Mra_Count, Counts_Json, Moved_From_Pending)
            VALUES (@Upload_Id, @Upload_Day, @By, @File_Name, @n, @Counts_Json, @Moved_Count);
            UPDATE dbo.SAP_MRA_CONTROL SET Data_Day = @Upload_Day, Is_Daily_Data_Upload = 1, Data_Version = @Upload_Id, Updated_At = SYSDATETIMEOFFSET()
             WHERE Id = 1;
            COMMIT TRANSACTION;
            RETURN;
        END

        IF @Cmd = N'STATE'
        BEGIN
            DECLARE @ver NVARCHAR(40);
            SELECT @ver = Data_Version FROM dbo.SAP_MRA_CONTROL WHERE Id = 1;
            SELECT Data_Day AS DataDay, CAST(CASE WHEN Is_Daily_Data_Upload = 1 AND Data_Day = @t THEN 1 ELSE 0 END AS BIT) AS IsDailyDataUpload,
                   Data_Version AS DataVersion
              FROM dbo.SAP_MRA_CONTROL WHERE Id = 1;
            SELECT TOP 1 Upload_Id AS UploadId, Upload_Day AS UploadDay, Uploaded_At AS UploadedAt, Uploaded_By AS UploadedBy,
                   File_Name AS FileName, Mra_Count AS MraCount, Counts_Json AS CountsJson, Moved_From_Pending AS MovedFromPending
              FROM dbo.SAP_MRA_FILE_UPLOADS ORDER BY Id DESC;
            SELECT Mra_No AS MraNo, Generic_Article AS Ga, Colour, Qty, Delivery_Date AS Dd, Mra_Level AS Level, Pos_Json AS PosJson,
                   Extra_Json AS ExtraJson
              FROM dbo.SAP_MRA_FILE ORDER BY Mra_No;
            SELECT Seq, Received_At AS ReceivedAt, Kind, By_User AS ByUser, Source_System AS SourceSystem, Raw_Json AS RawJson,
                   Msg_Json AS MsgJson, Error, Pending_Seq AS PendingSeq
              FROM dbo.SAP_MRA_FEED WHERE Data_Version = @ver ORDER BY Seq;
            SELECT Pending_Seq AS Seq, Received_At AS ReceivedAt, Kind, By_User AS ByUser, Source_System AS SourceSystem, Raw_Json AS RawJson,
                   Msg_Json AS MsgJson, Error
              FROM dbo.SAP_MRA_FEED_PENDING ORDER BY Pending_Seq;
            RETURN;
        END

        IF @Cmd = N'STAMP'
        BEGIN
            SELECT c.Data_Version AS DataVersion,
                   CAST(CASE WHEN c.Is_Daily_Data_Upload = 1 AND c.Data_Day = @t THEN 1 ELSE 0 END AS BIT) AS IsDailyDataUpload,
                   ISNULL((SELECT MAX(Seq) FROM dbo.SAP_MRA_FEED), 0) AS MaxSeq,
                   ISNULL((SELECT MAX(Pending_Seq) FROM dbo.SAP_MRA_FEED_PENDING), 0) AS MaxPendingSeq
              FROM dbo.SAP_MRA_CONTROL c WHERE c.Id = 1;
            RETURN;
        END

        IF @Cmd = N'FEED_ADD'
        BEGIN
            DECLARE @in TABLE (Idx INT NOT NULL PRIMARY KEY, Kind NVARCHAR(3) NOT NULL, RawJson NVARCHAR(MAX) NOT NULL,
                               MsgJson NVARCHAR(MAX) NULL, Error NVARCHAR(500) NULL);
            INSERT INTO @in (Idx, Kind, RawJson, MsgJson, Error)
            SELECT CAST(r.[key] AS INT), j.Kind, j.RawJson, j.MsgJson, j.Error
              FROM OPENJSON(@Rows) r
             CROSS APPLY OPENJSON(r.value) WITH (Kind NVARCHAR(3) '$.Kind', RawJson NVARCHAR(MAX) '$.RawJson',
                                                 MsgJson NVARCHAR(MAX) '$.MsgJson', Error NVARCHAR(500) '$.Error') j;
            DECLARE @out TABLE (Idx INT NOT NULL PRIMARY KEY, Seq BIGINT NOT NULL, Pending BIT NOT NULL);
            DECLARE @at DATETIMEOFFSET(7) = ISNULL(@Received_At, SYSDATETIMEOFFSET());
            BEGIN TRANSACTION;
            DECLARE @flag BIT, @cur NVARCHAR(40);
            SELECT @flag = CASE WHEN Is_Daily_Data_Upload = 1 AND Data_Day = @t THEN 1 ELSE 0 END, @cur = Data_Version
              FROM dbo.SAP_MRA_CONTROL WITH (UPDLOCK, HOLDLOCK) WHERE Id = 1;
            DECLARE @i INT = (SELECT MIN(Idx) FROM @in);
            WHILE @i IS NOT NULL
            BEGIN
                IF @flag = 1
                BEGIN
                    INSERT INTO dbo.SAP_MRA_FEED (Received_At, Received_Day, Kind, By_User, Source_System, Trace_Id, Raw_Json, Msg_Json, Error, Data_Version)
                    SELECT @at, CAST(@at AS DATE), Kind, @By, @Source_System, @Trace_Id, RawJson, MsgJson, Error, @cur FROM @in WHERE Idx = @i;
                    INSERT INTO @out (Idx, Seq, Pending) VALUES (@i, SCOPE_IDENTITY(), 0);
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.SAP_MRA_FEED_PENDING (Received_At, Received_Day, Kind, By_User, Source_System, Trace_Id, Raw_Json, Msg_Json, Error)
                    SELECT @at, CAST(@at AS DATE), Kind, @By, @Source_System, @Trace_Id, RawJson, MsgJson, Error FROM @in WHERE Idx = @i;
                    INSERT INTO @out (Idx, Seq, Pending) VALUES (@i, SCOPE_IDENTITY(), 1);
                END
                SET @i = (SELECT MIN(Idx) FROM @in WHERE Idx > @i);
            END
            COMMIT TRANSACTION;
            SELECT Idx, Seq, Pending FROM @out ORDER BY Idx;
            RETURN;
        END

        IF @Cmd = N'DECISIONS'
        BEGIN
            SELECT i.Id AS ItemId, i.Gm, i.Colour, i.Delivery_Month AS DeliveryMonth, i.Order_Qty AS OrderQty, i.Group_Qty AS GroupQty,
                   i.Verdict, i.Engine_Decision AS EngineDecision, i.Reason_Text AS ReasonText,
                   TRY_CAST(JSON_VALUE(i.Metrics_Json, '$.headroom_qty') AS FLOAT) AS MaxPermissible,
                   CAST(CASE WHEN i.Working_Json IS NULL THEN 0 ELSE 1 END AS BIT) AS HasWorking,
                   l.Decision_Id AS DecisionId, l.Request_Id AS RequestId, l.Po_Number AS PoNumber, l.Decided_At AS DecidedAt, l.Mode,
                   JSON_VALUE(l.Response_Json, '$.view_url') AS ViewUrl,
                   doc.Document_No AS DocumentNo
              FROM dbo.SAP_DECISION_ITEMS i
              JOIN dbo.SAP_DECISION_LOG l ON l.Id = i.Decision_Log_Id
              JOIN dbo.SAP_DECISION_DOCUMENTS doc ON doc.Decision_Log_Id = l.Id AND doc.Is_Current = 1   -- only the link: MRA number = document_no
             WHERE l.Http_Status = 200 AND l.Is_Test = 0 AND i.Engine_Decision IS NOT NULL
             ORDER BY l.Decided_At DESC, i.Id DESC;
            RETURN;
        END

        IF @Cmd = N'WORKING'
        BEGIN
            SELECT Working_Json AS WorkingJson FROM dbo.SAP_DECISION_ITEMS WHERE Id = @Item_Id;
            RETURN;
        END

        ;THROW 50001, N'PRO_SAP_MRA: unknown @Cmd.', 1;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* ───────────────────────────── schema version ───────────────────────────── */
IF NOT EXISTS (SELECT 1 FROM dbo.schema_info WHERE [key] = N'version')
    INSERT INTO dbo.schema_info ([key], value) VALUES (N'version', N'8');
UPDATE dbo.schema_info SET value = N'8' WHERE [key] = N'version' AND value < N'8';
GO

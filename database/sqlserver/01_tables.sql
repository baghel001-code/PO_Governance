/* ═══════════════════════════════════════════════════════════════════════════
   PO Governance — SQL Server schema v2 (run in the POGovernance database)
     sqlcmd -S <server> -d POGovernance -E -i 01_tables.sql
   Same tables and column names as the PostgreSQL schema v2 (database/schema.sql, v1.1.4 storage.py):
     json        -> NVARCHAR(MAX) + CHECK (ISJSON(...) = 1)   (the text is stored exactly as written)
     bytea       -> VARBINARY(MAX)
     timestamptz -> DATETIMEOFFSET(7) DEFAULT SYSDATETIMEOFFSET()
     bigserial   -> BIGINT IDENTITY(1,1)
     text        -> NVARCHAR(MAX); key columns NVARCHAR(n) COLLATE Latin1_General_100_BIN2
                    (case-sensitive, code-point order = PostgreSQL "C" collation / C# ordinal)
   Idempotent: every object is created only when missing. Safe to run again.
   ═══════════════════════════════════════════════════════════════════════════ */
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* documents: manifest, params, scr_mapping (one JSON row each) */
IF OBJECT_ID(N'dbo.app_settings', N'U') IS NULL
CREATE TABLE dbo.app_settings (
    name        NVARCHAR(450) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT PK_app_settings PRIMARY KEY,
    value       NVARCHAR(MAX) NOT NULL CONSTRAINT CK_app_settings_value_json CHECK (ISJSON(value) = 1),
    updated_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_app_settings_updated_at DEFAULT SYSDATETIMEOFFSET());
GO

/* "users" document, one row per login (only changed rows are written) */
IF OBJECT_ID(N'dbo.app_users', N'U') IS NULL
CREATE TABLE dbo.app_users (
    email       NVARCHAR(450) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT PK_app_users PRIMARY KEY,
    name        NVARCHAR(MAX) NULL,
    roles       NVARCHAR(MAX) NULL,
    active      BIT NULL,
    data        NVARCHAR(MAX) NOT NULL CONSTRAINT CK_app_users_data_json CHECK (ISJSON(data) = 1),
    updated_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_app_users_updated_at DEFAULT SYSDATETIMEOFFSET());
GO

/* "sessions" document, one row per token */
IF OBJECT_ID(N'dbo.app_sessions', N'U') IS NULL
CREATE TABLE dbo.app_sessions (
    token       NVARCHAR(450) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT PK_app_sessions PRIMARY KEY,
    email       NVARCHAR(MAX) NULL,
    data        NVARCHAR(MAX) NOT NULL CONSTRAINT CK_app_sessions_data_json CHECK (ISJSON(data) = 1),
    updated_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_app_sessions_updated_at DEFAULT SYSDATETIMEOFFSET());
GO

/* change counter per document, bumped on every document write */
IF OBJECT_ID(N'dbo.doc_stamps', N'U') IS NULL
CREATE TABLE dbo.doc_stamps (
    name        NVARCHAR(450) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT PK_doc_stamps PRIMARY KEY,
    stamp       BIGINT NOT NULL);
GO

/* "uploads" log */
IF OBJECT_ID(N'dbo.upload_log', N'U') IS NULL
CREATE TABLE dbo.upload_log (
    id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_upload_log PRIMARY KEY,
    rec         NVARCHAR(MAX) NOT NULL CONSTRAINT CK_upload_log_rec_json CHECK (ISJSON(rec) = 1),
    created_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_upload_log_created_at DEFAULT SYSDATETIMEOFFSET());
GO

/* "batches" log (Upload IDs) */
IF OBJECT_ID(N'dbo.upload_batches', N'U') IS NULL
CREATE TABLE dbo.upload_batches (
    id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_upload_batches PRIMARY KEY,
    upload_id   NVARCHAR(MAX) NULL,
    rec         NVARCHAR(MAX) NOT NULL CONSTRAINT CK_upload_batches_rec_json CHECK (ISJSON(rec) = 1),
    created_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_upload_batches_created_at DEFAULT SYSDATETIMEOFFSET());
GO

/* "runs" log (Tool 2) */
IF OBJECT_ID(N'dbo.run_index', N'U') IS NULL
CREATE TABLE dbo.run_index (
    id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_run_index PRIMARY KEY,
    run_id      NVARCHAR(MAX) NULL,
    rec         NVARCHAR(MAX) NOT NULL CONSTRAINT CK_run_index_rec_json CHECK (ISJSON(rec) = 1),
    created_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_run_index_created_at DEFAULT SYSDATETIMEOFFSET());
GO

/* "requests" log, one day per req_date (events request / approval / clear, replayed in id order) */
IF OBJECT_ID(N'dbo.request_events', N'U') IS NULL
CREATE TABLE dbo.request_events (
    id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_request_events PRIMARY KEY,
    req_date    NVARCHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    seq         INT NULL,
    event_type  NVARCHAR(MAX) NULL,
    req_id      NVARCHAR(MAX) NULL,
    rec         NVARCHAR(MAX) NOT NULL CONSTRAINT CK_request_events_rec_json CHECK (ISJSON(rec) = 1),
    created_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_request_events_created_at DEFAULT SYSDATETIMEOFFSET());
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'request_events_day' AND object_id = OBJECT_ID(N'dbo.request_events'))
    CREATE INDEX request_events_day ON dbo.request_events (req_date, id);
GO

/* audit trail, one line per change */
IF OBJECT_ID(N'dbo.audit_log', N'U') IS NULL
CREATE TABLE dbo.audit_log (
    id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_audit_log PRIMARY KEY,
    at          NVARCHAR(MAX) NOT NULL,
    who         NVARCHAR(MAX) NULL,
    action      NVARCHAR(MAX) NULL,
    detail      NVARCHAR(MAX) NULL);
GO

/* every uploaded data-file version (gzip JSON {"rows":[...],"meta":{...}}) */
IF OBJECT_ID(N'dbo.dataset_versions', N'U') IS NULL
CREATE TABLE dbo.dataset_versions (
    dkey        NVARCHAR(150) COLLATE Latin1_General_100_BIN2 NOT NULL,
    version     NVARCHAR(300) COLLATE Latin1_General_100_BIN2 NOT NULL,
    data        VARBINARY(MAX) NOT NULL,
    bytes       INT NULL,
    created_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_dataset_versions_created_at DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT PK_dataset_versions PRIMARY KEY (dkey, version));
GO

/* saved Tool 2 runs (gzip JSON); deleted runs are only flagged */
IF OBJECT_ID(N'dbo.run_files', N'U') IS NULL
CREATE TABLE dbo.run_files (
    run_id      NVARCHAR(450) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT PK_run_files PRIMARY KEY,
    data        VARBINARY(MAX) NOT NULL,
    bytes       INT NULL,
    deleted     BIT NOT NULL CONSTRAINT DF_run_files_deleted DEFAULT 0,
    created_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_run_files_created_at DEFAULT SYSDATETIMEOFFSET());
GO

/* "feedback" log */
IF OBJECT_ID(N'dbo.feedback_log', N'U') IS NULL
CREATE TABLE dbo.feedback_log (
    id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_feedback_log PRIMARY KEY,
    fb_id       NVARCHAR(MAX) NULL,
    rec         NVARCHAR(MAX) NOT NULL CONSTRAINT CK_feedback_log_rec_json CHECK (ISJSON(rec) = 1),
    created_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_feedback_log_created_at DEFAULT SYSDATETIMEOFFSET());
GO

/* schema version (03_seed.sql writes version = 2) */
IF OBJECT_ID(N'dbo.schema_info', N'U') IS NULL
CREATE TABLE dbo.schema_info (
    [key]       NVARCHAR(450) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT PK_schema_info PRIMARY KEY,
    value       NVARCHAR(MAX) NULL);
GO

/* ═══════════════════════════════════════════════════════════════════════════
   PO Governance — SQL Server stored procedures (run in the POGovernance database, after 01_tables.sql)
     sqlcmd -S <server> -d POGovernance -E -i 02_procedures.sql
   One procedure per IStore operation group (Infrastructure/Repositories/SqlServerStore.cs), QPS style:
     * name PRO_POGOV_<AREA>[_<VERB>], a @Cmd switch where one procedure serves several reads;
     * every procedure has @Error_Code INT OUTPUT / @Error_Message NVARCHAR(500) OUTPUT
       (0 / '' = success) and a TRY/CATCH that rolls back and returns ERROR_NUMBER() / ERROR_MESSAGE();
     * own error numbers: 50001 unknown @Cmd / stream / kind, 50002 refused (TRUNCATE without confirm),
       50409 stale document stamp (map document changed by another API instance; nothing written).
   JSON text is stored and returned unchanged (the API writes Python json.dumps formatting).
   Lists of rows are passed as one JSON array (NVARCHAR(MAX)) and expanded with OPENJSON, in array order.
   CREATE OR ALTER: safe to run again (e.g. after an upgrade).
   ═══════════════════════════════════════════════════════════════════════════ */
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ───────────────────────────── documents ───────────────────────────── */

/* Read a document.
   @Cmd  GET_DOC      -> (K, V) row of app_settings for @Name (manifest, params, scr_mapping)
         GET_USERS    -> (K = email, V = data) rows of app_users ordered by email
         GET_SESSIONS -> (K = token, V = data) rows of app_sessions ordered by token
         GET_STAMP    -> no rows
   @Stamp OUTPUT: doc_stamps.stamp of @Name (0 = never written). Read before the rows, so a writer that
   commits in between makes the stamp look older (never newer) than the rows returned. */
CREATE OR ALTER PROCEDURE dbo.PRO_POGOV_DOC_GET
    @Cmd            NVARCHAR(50),
    @Name           NVARCHAR(450),
    @Stamp          BIGINT = NULL OUTPUT,
    @Error_Code     INT = NULL OUTPUT,
    @Error_Message  NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        SELECT @Stamp = ISNULL((SELECT stamp FROM dbo.doc_stamps WHERE name = @Name), 0);
        IF @Cmd = N'GET_DOC'
            SELECT name AS K, value AS V FROM dbo.app_settings WHERE name = @Name;
        ELSE IF @Cmd = N'GET_USERS'
            SELECT email AS K, data AS V FROM dbo.app_users ORDER BY email;
        ELSE IF @Cmd = N'GET_SESSIONS'
            SELECT token AS K, data AS V FROM dbo.app_sessions ORDER BY token;
        ELSE IF @Cmd <> N'GET_STAMP' OR @Cmd IS NULL
            THROW 50001, N'PRO_POGOV_DOC_GET: unknown @Cmd.', 1;
    END TRY
    BEGIN CATCH
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* Write a whole document row of app_settings (manifest, params, scr_mapping) and bump its stamp,
   in one transaction. @Stamp OUTPUT = the new stamp. */
CREATE OR ALTER PROCEDURE dbo.PRO_POGOV_DOC_PUT
    @Name           NVARCHAR(450),
    @Value          NVARCHAR(MAX),
    @Stamp          BIGINT = NULL OUTPUT,
    @Error_Code     INT = NULL OUTPUT,
    @Error_Message  NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE dbo.app_settings WITH (UPDLOCK, HOLDLOCK)
           SET value = @Value, updated_at = SYSDATETIMEOFFSET()
         WHERE name = @Name;
        IF @@ROWCOUNT = 0
            INSERT INTO dbo.app_settings (name, value, updated_at) VALUES (@Name, @Value, SYSDATETIMEOFFSET());

        UPDATE dbo.doc_stamps WITH (UPDLOCK, HOLDLOCK) SET @Stamp = stamp = stamp + 1 WHERE name = @Name;
        IF @@ROWCOUNT = 0
        BEGIN
            INSERT INTO dbo.doc_stamps (name, stamp) VALUES (@Name, 1);
            SET @Stamp = 1;
        END
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* Write the changed rows of a map document ("users" -> app_users, "sessions" -> app_sessions) and bump
   its stamp, in one transaction. Only the rows that changed are sent (the API compares with what it last
   read / wrote):
     @Cmd      USERS | SESSIONS
     @Rows     JSON array of upserts:  USERS    [{"k": email, "name": .., "roles": "a,b", "active": true, "v": "<data json text>"}]
                                       SESSIONS [{"k": token, "email": .., "v": "<data json text>"}]
     @Deleted  JSON array of keys to delete: ["k1", "k2"]
     @Expected_Stamp  when not NULL and the current stamp differs (another API instance wrote the document
                      since this one read it): nothing is written, @Error_Code = 50409, @Stamp = current stamp.
   @Stamp OUTPUT = the new stamp. */
CREATE OR ALTER PROCEDURE dbo.PRO_POGOV_DOC_MAP_PUT
    @Cmd            NVARCHAR(50),
    @Rows           NVARCHAR(MAX) = N'[]',
    @Deleted        NVARCHAR(MAX) = N'[]',
    @Expected_Stamp BIGINT = NULL,
    @Stamp          BIGINT = NULL OUTPUT,
    @Error_Code     INT = NULL OUTPUT,
    @Error_Message  NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    DECLARE @Name NVARCHAR(450) = CASE @Cmd WHEN N'USERS' THEN N'users' WHEN N'SESSIONS' THEN N'sessions' END;
    BEGIN TRY
        IF @Name IS NULL THROW 50001, N'PRO_POGOV_DOC_MAP_PUT: unknown @Cmd.', 1;
        SET @Rows = ISNULL(@Rows, N'[]');
        SET @Deleted = ISNULL(@Deleted, N'[]');

        BEGIN TRANSACTION;
        DECLARE @Cur BIGINT = ISNULL((SELECT stamp FROM dbo.doc_stamps WITH (UPDLOCK, HOLDLOCK) WHERE name = @Name), 0);
        IF @Expected_Stamp IS NOT NULL AND @Cur <> @Expected_Stamp
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT @Stamp = @Cur, @Error_Code = 50409,
                   @Error_Message = N'Document ' + @Name + N' was changed by another process (stamp ' +
                                    CAST(@Cur AS NVARCHAR(30)) + N', expected ' + CAST(@Expected_Stamp AS NVARCHAR(30)) + N').';
            RETURN;
        END

        IF @Cmd = N'USERS'
        BEGIN
            DELETE u FROM dbo.app_users AS u
             WHERE u.email IN (SELECT d.k COLLATE Latin1_General_100_BIN2 FROM OPENJSON(@Deleted) WITH (k NVARCHAR(450) N'$') AS d);

            MERGE dbo.app_users WITH (HOLDLOCK) AS t
            USING (SELECT j.k COLLATE Latin1_General_100_BIN2 AS k, j.name, j.roles, j.active, j.v
                     FROM OPENJSON(@Rows) WITH (k NVARCHAR(450) N'$.k', name NVARCHAR(MAX) N'$.name', roles NVARCHAR(MAX) N'$.roles',
                                                active BIT N'$.active', v NVARCHAR(MAX) N'$.v') AS j) AS s
               ON t.email = s.k
            WHEN MATCHED THEN
                UPDATE SET name = s.name, roles = s.roles, active = s.active, data = s.v, updated_at = SYSDATETIMEOFFSET()
            WHEN NOT MATCHED BY TARGET THEN
                INSERT (email, name, roles, active, data, updated_at) VALUES (s.k, s.name, s.roles, s.active, s.v, SYSDATETIMEOFFSET());
        END
        ELSE
        BEGIN
            DELETE x FROM dbo.app_sessions AS x
             WHERE x.token IN (SELECT d.k COLLATE Latin1_General_100_BIN2 FROM OPENJSON(@Deleted) WITH (k NVARCHAR(450) N'$') AS d);

            MERGE dbo.app_sessions WITH (HOLDLOCK) AS t
            USING (SELECT j.k COLLATE Latin1_General_100_BIN2 AS k, j.email, j.v
                     FROM OPENJSON(@Rows) WITH (k NVARCHAR(450) N'$.k', email NVARCHAR(MAX) N'$.email', v NVARCHAR(MAX) N'$.v') AS j) AS s
               ON t.token = s.k
            WHEN MATCHED THEN
                UPDATE SET email = s.email, data = s.v, updated_at = SYSDATETIMEOFFSET()
            WHEN NOT MATCHED BY TARGET THEN
                INSERT (token, email, data, updated_at) VALUES (s.k, s.email, s.v, SYSDATETIMEOFFSET());
        END

        UPDATE dbo.doc_stamps SET @Stamp = stamp = stamp + 1 WHERE name = @Name;
        IF @@ROWCOUNT = 0
        BEGIN
            INSERT INTO dbo.doc_stamps (name, stamp) VALUES (@Name, 1);
            SET @Stamp = 1;
        END
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* ───────────────────────────── logs ───────────────────────────── */
/* Streams: uploads -> upload_log, batches -> upload_batches, runs -> run_index, feedback -> feedback_log,
   requests -> request_events (one "day" = req_date). Records are returned in insertion (id) order. */

/* @Cmd  READ       -> (D, Rec) of @Stream; for "requests" only the day @Day
         READ_DAYS  -> (D, Rec) of "requests" for the days in @Days (JSON array of strings), id order
         DAYS       -> (D) distinct request days, ascending */
CREATE OR ALTER PROCEDURE dbo.PRO_POGOV_LOG_READ
    @Cmd            NVARCHAR(50),
    @Stream         NVARCHAR(50) = N'requests',
    @Day            NVARCHAR(64) = NULL,
    @Days           NVARCHAR(MAX) = NULL,
    @Error_Code     INT = NULL OUTPUT,
    @Error_Message  NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        IF @Cmd = N'READ'
        BEGIN
            IF @Stream = N'requests'
                SELECT req_date AS D, rec AS Rec FROM dbo.request_events WHERE req_date = @Day ORDER BY id;
            ELSE IF @Stream = N'uploads'
                SELECT CAST(NULL AS NVARCHAR(64)) AS D, rec AS Rec FROM dbo.upload_log ORDER BY id;
            ELSE IF @Stream = N'batches'
                SELECT CAST(NULL AS NVARCHAR(64)) AS D, rec AS Rec FROM dbo.upload_batches ORDER BY id;
            ELSE IF @Stream = N'runs'
                SELECT CAST(NULL AS NVARCHAR(64)) AS D, rec AS Rec FROM dbo.run_index ORDER BY id;
            ELSE IF @Stream = N'feedback'
                SELECT CAST(NULL AS NVARCHAR(64)) AS D, rec AS Rec FROM dbo.feedback_log ORDER BY id;
            ELSE
                THROW 50001, N'PRO_POGOV_LOG_READ: unknown @Stream.', 1;
        END
        ELSE IF @Cmd = N'READ_DAYS'
            SELECT e.req_date AS D, e.rec AS Rec
              FROM dbo.request_events AS e
             WHERE e.req_date IN (SELECT d.v COLLATE Latin1_General_100_BIN2 FROM OPENJSON(ISNULL(@Days, N'[]')) WITH (v NVARCHAR(64) N'$') AS d)
             ORDER BY e.id;
        ELSE IF @Cmd = N'DAYS'
            SELECT DISTINCT req_date AS D FROM dbo.request_events ORDER BY req_date;
        ELSE
            THROW 50001, N'PRO_POGOV_LOG_READ: unknown @Cmd.', 1;
    END TRY
    BEGIN CATCH
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* Append one record. @Ref_Id = upload_id (batches) / run_id (runs) / fb_id (feedback) / req_id (requests);
   @Seq and @Event_Type only for requests. */
CREATE OR ALTER PROCEDURE dbo.PRO_POGOV_LOG_APPEND
    @Stream         NVARCHAR(50),
    @Rec            NVARCHAR(MAX),
    @Day            NVARCHAR(64) = NULL,
    @Ref_Id         NVARCHAR(MAX) = NULL,
    @Seq            INT = NULL,
    @Event_Type     NVARCHAR(MAX) = NULL,
    @Error_Code     INT = NULL OUTPUT,
    @Error_Message  NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        IF @Stream = N'requests'
            INSERT INTO dbo.request_events (req_date, seq, event_type, req_id, rec) VALUES (@Day, @Seq, @Event_Type, @Ref_Id, @Rec);
        ELSE IF @Stream = N'uploads'
            INSERT INTO dbo.upload_log (rec) VALUES (@Rec);
        ELSE IF @Stream = N'batches'
            INSERT INTO dbo.upload_batches (upload_id, rec) VALUES (@Ref_Id, @Rec);
        ELSE IF @Stream = N'runs'
            INSERT INTO dbo.run_index (run_id, rec) VALUES (@Ref_Id, @Rec);
        ELSE IF @Stream = N'feedback'
            INSERT INTO dbo.feedback_log (fb_id, rec) VALUES (@Ref_Id, @Rec);
        ELSE
            THROW 50001, N'PRO_POGOV_LOG_APPEND: unknown @Stream.', 1;
    END TRY
    BEGIN CATCH
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* Replace a whole stream (or one day of "requests") in one transaction.
   @Rows = JSON array, in order: [{"rec": "<record json text>", "ref": .., "seq": .., "type": ..}] */
CREATE OR ALTER PROCEDURE dbo.PRO_POGOV_LOG_REWRITE
    @Stream         NVARCHAR(50),
    @Day            NVARCHAR(64) = NULL,
    @Rows           NVARCHAR(MAX) = N'[]',
    @Error_Code     INT = NULL OUTPUT,
    @Error_Message  NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        IF @Stream IS NULL OR @Stream NOT IN (N'requests', N'uploads', N'batches', N'runs', N'feedback')
            THROW 50001, N'PRO_POGOV_LOG_REWRITE: unknown @Stream.', 1;
        SET @Rows = ISNULL(@Rows, N'[]');

        BEGIN TRANSACTION;
        IF @Stream = N'requests'
        BEGIN
            DELETE FROM dbo.request_events WHERE req_date = @Day;
            INSERT INTO dbo.request_events (req_date, seq, event_type, req_id, rec)
            SELECT @Day, j.seq, j.event_type, j.ref, j.rec
              FROM OPENJSON(@Rows) AS a
             CROSS APPLY OPENJSON(a.value) WITH (rec NVARCHAR(MAX) N'$.rec', ref NVARCHAR(MAX) N'$.ref', seq INT N'$.seq', event_type NVARCHAR(MAX) N'$.type') AS j
             ORDER BY CAST(a.[key] AS INT);
        END
        ELSE IF @Stream = N'uploads'
        BEGIN
            DELETE FROM dbo.upload_log;
            INSERT INTO dbo.upload_log (rec)
            SELECT j.rec FROM OPENJSON(@Rows) AS a CROSS APPLY OPENJSON(a.value) WITH (rec NVARCHAR(MAX) N'$.rec') AS j
             ORDER BY CAST(a.[key] AS INT);
        END
        ELSE IF @Stream = N'batches'
        BEGIN
            DELETE FROM dbo.upload_batches;
            INSERT INTO dbo.upload_batches (upload_id, rec)
            SELECT j.ref, j.rec FROM OPENJSON(@Rows) AS a CROSS APPLY OPENJSON(a.value) WITH (rec NVARCHAR(MAX) N'$.rec', ref NVARCHAR(MAX) N'$.ref') AS j
             ORDER BY CAST(a.[key] AS INT);
        END
        ELSE IF @Stream = N'runs'
        BEGIN
            DELETE FROM dbo.run_index;
            INSERT INTO dbo.run_index (run_id, rec)
            SELECT j.ref, j.rec FROM OPENJSON(@Rows) AS a CROSS APPLY OPENJSON(a.value) WITH (rec NVARCHAR(MAX) N'$.rec', ref NVARCHAR(MAX) N'$.ref') AS j
             ORDER BY CAST(a.[key] AS INT);
        END
        ELSE
        BEGIN
            DELETE FROM dbo.feedback_log;
            INSERT INTO dbo.feedback_log (fb_id, rec)
            SELECT j.ref, j.rec FROM OPENJSON(@Rows) AS a CROSS APPLY OPENJSON(a.value) WITH (rec NVARCHAR(MAX) N'$.rec', ref NVARCHAR(MAX) N'$.ref') AS j
             ORDER BY CAST(a.[key] AS INT);
        END
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* Delete one day of the "requests" stream (history retention). */
CREATE OR ALTER PROCEDURE dbo.PRO_POGOV_LOG_DELETE_DAY
    @Stream         NVARCHAR(50) = N'requests',
    @Day            NVARCHAR(64),
    @Error_Code     INT = NULL OUTPUT,
    @Error_Message  NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        DELETE FROM dbo.request_events WHERE req_date = @Day;
    END TRY
    BEGIN CATCH
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* ───────────────────────────── audit ───────────────────────────── */

/* @Cmd  APPEND       one line (@At, @Who, @Action, @Detail)
         APPEND_MANY  @Rows = JSON array [{"at": .., "who": .., "action": .., "detail": ..}] in one transaction
         READ         -> (At, Who, Action, Detail) in id order */
CREATE OR ALTER PROCEDURE dbo.PRO_POGOV_AUDIT
    @Cmd            NVARCHAR(50),
    @At             NVARCHAR(MAX) = NULL,
    @Who            NVARCHAR(MAX) = NULL,
    @Action         NVARCHAR(MAX) = NULL,
    @Detail         NVARCHAR(MAX) = NULL,
    @Rows           NVARCHAR(MAX) = NULL,
    @Error_Code     INT = NULL OUTPUT,
    @Error_Message  NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        IF @Cmd = N'APPEND'
            INSERT INTO dbo.audit_log (at, who, action, detail) VALUES (@At, @Who, @Action, @Detail);
        ELSE IF @Cmd = N'APPEND_MANY'
        BEGIN
            BEGIN TRANSACTION;
            INSERT INTO dbo.audit_log (at, who, action, detail)
            SELECT j.at, j.who, j.action, j.detail
              FROM OPENJSON(ISNULL(@Rows, N'[]')) AS a
             CROSS APPLY OPENJSON(a.value) WITH (at NVARCHAR(MAX) N'$.at', who NVARCHAR(MAX) N'$.who',
                                                 action NVARCHAR(MAX) N'$.action', detail NVARCHAR(MAX) N'$.detail') AS j
             ORDER BY CAST(a.[key] AS INT);
            COMMIT TRANSACTION;
        END
        ELSE IF @Cmd = N'READ'
            SELECT at AS [At], who AS Who, action AS [Action], detail AS Detail FROM dbo.audit_log ORDER BY id;
        ELSE
            THROW 50001, N'PRO_POGOV_AUDIT: unknown @Cmd.', 1;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* ───────────────────────────── blobs ───────────────────────────── */
/* @Kind  ds  -> dataset_versions (@Key = dataset key, @Version = version)
          run -> run_files        (@Version = run id; @Key ignored) */

/* Insert or replace a blob. @Created_At: only the migration passes it (keeps the original upload time,
   used by history retention); otherwise a new row gets now() and a replaced row keeps its time. */
CREATE OR ALTER PROCEDURE dbo.PRO_POGOV_BLOB_PUT
    @Kind           NVARCHAR(10),
    @Key            NVARCHAR(150) = N'',
    @Version        NVARCHAR(450),
    @Data           VARBINARY(MAX),
    @Deleted        BIT = 0,
    @Created_At     DATETIMEOFFSET(7) = NULL,
    @Error_Code     INT = NULL OUTPUT,
    @Error_Message  NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        BEGIN TRANSACTION;
        IF @Kind = N'ds'
        BEGIN
            UPDATE dbo.dataset_versions WITH (UPDLOCK, HOLDLOCK)
               SET data = @Data, bytes = DATALENGTH(@Data), created_at = ISNULL(@Created_At, created_at)
             WHERE dkey = @Key AND version = @Version;
            IF @@ROWCOUNT = 0
                INSERT INTO dbo.dataset_versions (dkey, version, data, bytes, created_at)
                VALUES (@Key, @Version, @Data, DATALENGTH(@Data), ISNULL(@Created_At, SYSDATETIMEOFFSET()));
        END
        ELSE IF @Kind = N'run'
        BEGIN
            UPDATE dbo.run_files WITH (UPDLOCK, HOLDLOCK)
               SET data = @Data, bytes = DATALENGTH(@Data), deleted = ISNULL(@Deleted, 0), created_at = ISNULL(@Created_At, created_at)
             WHERE run_id = @Version;
            IF @@ROWCOUNT = 0
                INSERT INTO dbo.run_files (run_id, data, bytes, deleted, created_at)
                VALUES (@Version, @Data, DATALENGTH(@Data), ISNULL(@Deleted, 0), ISNULL(@Created_At, SYSDATETIMEOFFSET()));
        END
        ELSE
            THROW 50001, N'PRO_POGOV_BLOB_PUT: unknown @Kind.', 1;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* @Cmd  GET      -> (Data); runs only when not deleted
         GET_ANY  -> (Data); runs with deleted = @Deleted (datasets: same as GET)
         SIZE     -> (Bytes); runs only when not deleted (no row = missing)
         LIST     -> (K, V, D, T) all blobs of @Kind, T = created_at as epoch seconds (float);
                     ds ordered by dkey, version; run ordered by run_id (K = '') */
CREATE OR ALTER PROCEDURE dbo.PRO_POGOV_BLOB_GET
    @Cmd            NVARCHAR(50),
    @Kind           NVARCHAR(10),
    @Key            NVARCHAR(150) = N'',
    @Version        NVARCHAR(450) = NULL,
    @Deleted        BIT = 0,
    @Error_Code     INT = NULL OUTPUT,
    @Error_Message  NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    DECLARE @Epoch DATETIMEOFFSET(7) = CAST(N'1970-01-01T00:00:00+00:00' AS DATETIMEOFFSET(7));
    BEGIN TRY
        IF @Kind NOT IN (N'ds', N'run') OR @Kind IS NULL
            THROW 50001, N'PRO_POGOV_BLOB_GET: unknown @Kind.', 1;

        IF @Cmd = N'GET' OR (@Cmd = N'GET_ANY' AND @Kind = N'ds')
        BEGIN
            IF @Kind = N'ds'
                SELECT data AS Data FROM dbo.dataset_versions WHERE dkey = @Key AND version = @Version;
            ELSE
                SELECT data AS Data FROM dbo.run_files WHERE run_id = @Version AND deleted = 0;
        END
        ELSE IF @Cmd = N'GET_ANY'
            SELECT data AS Data FROM dbo.run_files WHERE run_id = @Version AND deleted = ISNULL(@Deleted, 0);
        ELSE IF @Cmd = N'SIZE'
        BEGIN
            IF @Kind = N'ds'
                SELECT bytes AS Bytes FROM dbo.dataset_versions WHERE dkey = @Key AND version = @Version;
            ELSE
                SELECT bytes AS Bytes FROM dbo.run_files WHERE run_id = @Version AND deleted = 0;
        END
        ELSE IF @Cmd = N'LIST'
        BEGIN
            IF @Kind = N'ds'
                SELECT dkey AS K, version AS V, CAST(0 AS BIT) AS D,
                       CAST(DATEDIFF_BIG(MICROSECOND, @Epoch, created_at) AS FLOAT) / 1000000.0 AS T
                  FROM dbo.dataset_versions ORDER BY dkey, version;
            ELSE
                SELECT CAST(N'' AS NVARCHAR(150)) AS K, run_id AS V, deleted AS D,
                       CAST(DATEDIFF_BIG(MICROSECOND, @Epoch, created_at) AS FLOAT) / 1000000.0 AS T
                  FROM dbo.run_files ORDER BY run_id;
        END
        ELSE
            THROW 50001, N'PRO_POGOV_BLOB_GET: unknown @Cmd.', 1;
    END TRY
    BEGIN CATCH
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* @Cmd  MARK_DELETED  runs: set deleted = 1 (datasets: nothing)
         DELETE        remove the row */
CREATE OR ALTER PROCEDURE dbo.PRO_POGOV_BLOB_DELETE
    @Cmd            NVARCHAR(50),
    @Kind           NVARCHAR(10),
    @Key            NVARCHAR(150) = N'',
    @Version        NVARCHAR(450),
    @Error_Code     INT = NULL OUTPUT,
    @Error_Message  NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        IF @Kind NOT IN (N'ds', N'run') OR @Kind IS NULL
            THROW 50001, N'PRO_POGOV_BLOB_DELETE: unknown @Kind.', 1;
        IF @Cmd = N'MARK_DELETED'
        BEGIN
            IF @Kind = N'run' UPDATE dbo.run_files SET deleted = 1 WHERE run_id = @Version;
        END
        ELSE IF @Cmd = N'DELETE'
        BEGIN
            IF @Kind = N'ds'
                DELETE FROM dbo.dataset_versions WHERE dkey = @Key AND version = @Version;
            ELSE
                DELETE FROM dbo.run_files WHERE run_id = @Version;
        END
        ELSE
            THROW 50001, N'PRO_POGOV_BLOB_DELETE: unknown @Cmd.', 1;
    END TRY
    BEGIN CATCH
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* ───────────────────────────── misc ───────────────────────────── */

/* @Cmd  STORAGE_BYTES   -> (Value BIGINT) size of the database files (data + log), bytes
         IS_EMPTY        -> (Value BIGINT) 1 when there are no documents, users, data files, requests, feedback
         SCHEMA_VERSION  -> (Value NVARCHAR) schema_info version ('2') */
CREATE OR ALTER PROCEDURE dbo.PRO_POGOV_STORE_INFO
    @Cmd            NVARCHAR(50),
    @Error_Code     INT = NULL OUTPUT,
    @Error_Message  NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        IF @Cmd = N'STORAGE_BYTES'
            SELECT CAST(SUM(CAST(size AS BIGINT)) * 8192 AS BIGINT) AS Value FROM sys.database_files;
        ELSE IF @Cmd = N'IS_EMPTY'
            SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.app_settings) OR EXISTS (SELECT 1 FROM dbo.app_users)
                               OR EXISTS (SELECT 1 FROM dbo.dataset_versions) OR EXISTS (SELECT 1 FROM dbo.request_events)
                               OR EXISTS (SELECT 1 FROM dbo.feedback_log) THEN 0 ELSE 1 END AS BIGINT) AS Value;
        ELSE IF @Cmd = N'SCHEMA_VERSION'
            SELECT value AS Value FROM dbo.schema_info WHERE [key] = N'version';
        ELSE
            THROW 50001, N'PRO_POGOV_STORE_INFO: unknown @Cmd.', 1;
    END TRY
    BEGIN CATCH
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* Empty every data table (schema_info stays) in one transaction — used by the migration tool with --force.
   @Confirm must be 'TRUNCATE ALL'. DELETE (not TRUNCATE) so EXECUTE permission is enough. */
CREATE OR ALTER PROCEDURE dbo.PRO_POGOV_TRUNCATE_ALL
    @Confirm        NVARCHAR(50),
    @Error_Code     INT = NULL OUTPUT,
    @Error_Message  NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        IF @Confirm IS NULL OR @Confirm <> N'TRUNCATE ALL'
            THROW 50002, N'PRO_POGOV_TRUNCATE_ALL: pass @Confirm = ''TRUNCATE ALL''.', 1;
        BEGIN TRANSACTION;
        DELETE FROM dbo.app_settings;
        DELETE FROM dbo.app_users;
        DELETE FROM dbo.app_sessions;
        DELETE FROM dbo.doc_stamps;
        DELETE FROM dbo.upload_log;
        DELETE FROM dbo.upload_batches;
        DELETE FROM dbo.run_index;
        DELETE FROM dbo.request_events;
        DELETE FROM dbo.audit_log;
        DELETE FROM dbo.dataset_versions;
        DELETE FROM dbo.run_files;
        DELETE FROM dbo.feedback_log;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

/* The cross-process write lock (server.py LOCK, IStore.LockAsync), shared by every API instance on
   this database. Session-owned application lock: the API calls GET and RELEASE on one dedicated
   connection that it keeps open while it holds the lock; if that connection or the API process dies,
   SQL Server ends the session and releases the lock automatically.
     @Cmd  GET      wait up to @Timeout_Ms; @Result = 0 / 1 granted, -1 timed out (call again)
           RELEASE  @Result = 0 released
           STATE    @Result = 1 when this session holds the lock, else 0 */
CREATE OR ALTER PROCEDURE dbo.PRO_POGOV_APP_LOCK
    @Cmd            NVARCHAR(50),
    @Resource       NVARCHAR(255) = N'POGovernance.ServerLock',
    @Timeout_Ms     INT = 10000,
    @Result         INT = NULL OUTPUT,
    @Error_Code     INT = NULL OUTPUT,
    @Error_Message  NVARCHAR(500) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @Error_Code = 0, @Error_Message = N'';
    BEGIN TRY
        IF @Cmd = N'GET'
        BEGIN
            EXEC @Result = sys.sp_getapplock @Resource = @Resource, @LockMode = N'Exclusive', @LockOwner = N'Session',
                                             @LockTimeout = @Timeout_Ms, @DbPrincipal = N'public';
            IF @Result < -1
                SELECT @Error_Code = 50000 - @Result,
                       @Error_Message = N'sp_getapplock failed (' + CAST(@Result AS NVARCHAR(10)) + N': -2 cancelled, -3 deadlock victim, -999 error).';
        END
        ELSE IF @Cmd = N'RELEASE'
            EXEC @Result = sys.sp_releaseapplock @Resource = @Resource, @LockOwner = N'Session', @DbPrincipal = N'public';
        ELSE IF @Cmd = N'STATE'
            SET @Result = CASE WHEN APPLOCK_MODE(N'public', @Resource, N'Session') = N'Exclusive' THEN 1 ELSE 0 END;
        ELSE
            THROW 50001, N'PRO_POGOV_APP_LOCK: unknown @Cmd.', 1;
    END TRY
    BEGIN CATCH
        SELECT @Error_Code = ERROR_NUMBER(), @Error_Message = LEFT(ERROR_MESSAGE(), 500);
    END CATCH
END
GO

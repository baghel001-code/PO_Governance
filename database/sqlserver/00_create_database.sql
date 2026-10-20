/* ═══════════════════════════════════════════════════════════════════════════
   PO Governance — SQL Server: create the database (run once, as sysadmin, in master)
     sqlcmd -S <server> -E -i 00_create_database.sql            (Windows login)
     sqlcmd -S <server> -U sa -P <pw> -C -i 00_create_database.sql
   Then run 01_tables.sql, 02_procedures.sql, 03_seed.sql with -d POGovernance.
   Idempotent: does nothing when the database already exists.
   ═══════════════════════════════════════════════════════════════════════════ */
SET NOCOUNT ON;
GO
IF DB_ID(N'POGovernance') IS NULL
BEGIN
    CREATE DATABASE POGovernance;
    PRINT 'Database POGovernance created.';
END
ELSE
    PRINT 'Database POGovernance already exists.';
GO
/* Readers (lists, history, status) never wait for writers: row versioning for READ COMMITTED. */
IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'POGovernance' AND is_read_committed_snapshot_on = 0)
    ALTER DATABASE POGovernance SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO
/* Recovery model: the server default (model database) is kept. With FULL, schedule log backups;
   without them use:  ALTER DATABASE POGovernance SET RECOVERY SIMPLE;  */

/* Optional: a SQL login for the API (instead of a Windows / integrated login).
   The API needs only EXECUTE on the PRO_POGOV_* procedures (they own the table access).

USE master;
IF SUSER_ID(N'vmm_app') IS NULL CREATE LOGIN vmm_app WITH PASSWORD = N'<strong password>', CHECK_POLICY = ON;
GO
USE POGovernance;
IF USER_ID(N'vmm_app') IS NULL CREATE USER vmm_app FOR LOGIN vmm_app;
GRANT EXECUTE ON SCHEMA::dbo TO vmm_app;
GO
*/

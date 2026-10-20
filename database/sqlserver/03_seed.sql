/* ═══════════════════════════════════════════════════════════════════════════
   PO Governance — SQL Server seed (run in the POGovernance database, after 02_procedures.sql)
     sqlcmd -S <server> -d POGovernance -E -i 03_seed.sql
   schema_info version = 2 (same as the PostgreSQL schema v2). The admin login is NOT seeded here:
   the API creates PoGovSettings:AdminEmail (Admin + Planner, default password, must change) at start-up.
   ═══════════════════════════════════════════════════════════════════════════ */
SET NOCOUNT ON;
GO
IF NOT EXISTS (SELECT 1 FROM dbo.schema_info WHERE [key] = N'version')
    INSERT INTO dbo.schema_info ([key], value) VALUES (N'version', N'2');
UPDATE dbo.schema_info SET value = N'2' WHERE [key] = N'version' AND value < N'2';
GO
IF NOT EXISTS (SELECT 1 FROM dbo.schema_info WHERE [key] = N'engine')
    INSERT INTO dbo.schema_info ([key], value) VALUES (N'engine', N'sqlserver');
GO

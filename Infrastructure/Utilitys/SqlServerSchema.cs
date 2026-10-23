using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Infrastructure.Utilitys
{
    /// <summary>
    /// Runs the SQL Server scripts of database/sqlserver (embedded in this assembly) the way sqlcmd does:
    /// split on "GO" lines, one batch at a time. 01_tables.sql, 02_procedures.sql, 03_seed.sql, 04_api_master.sql are idempotent.
    /// Used by the tests, the migration tool (--create-schema) and, when PoGovSettings:SqlServerCreateSchema
    /// is true, by the API at start-up.
    /// </summary>
    public static class SqlServerSchema
    {
        /// <summary>Scripts run in the application database, in order (00_create_database.sql runs in master).</summary>
        public static readonly string[] DatabaseScripts = { "01_tables.sql", "02_procedures.sql", "03_seed.sql", ApiMasterScript, SapInterfaceScript };

        /// <summary>API_MASTER + PRO_GET_ALL_API + seed (QPS API master list the portal loads at start-up).</summary>
        public const string ApiMasterScript = "04_api_master.sql";

        /// <summary>SAP R/3 interface tables + PRO_SAP_* procedures + reason-code seed; sets schema_info version 3.</summary>
        public const string SapInterfaceScript = "05_sap_interface.sql";

        /// <summary>The schema version before the SAP interface (01..04 only): still usable by the portal.</summary>
        public const string MinimumVersion = "2";

        private static readonly Regex ApiBaseLine = new(@"(DECLARE @ApiBase\s+NVARCHAR\(400\)\s*=\s*N')[^']*(')", RegexOptions.Compiled);
        private static readonly Regex ApiEnvLine = new(@"(DECLARE @ApiEnv\s+NVARCHAR\(30\)\s*=\s*N')[^']*(')", RegexOptions.Compiled);

        /// <summary>
        /// 04_api_master.sql with another API base URL (ApiMaster:BaseUrl) and environment (the API's environment) in its
        /// seed: @ApiBase becomes the API_BASE_URL row of @ApiEnv.
        /// </summary>
        public static string ApiMasterSql(string? apiBaseUrl, string? apiEnvironment = null)
        {
            var sql = ReadScript(ApiMasterScript);
            if (!string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                var b = apiBaseUrl.Trim();
                if (!b.EndsWith('/')) b += "/";
                sql = ApiBaseLine.Replace(sql, m => m.Groups[1].Value + b.Replace("'", "''") + m.Groups[2].Value, 1);
            }
            if (!string.IsNullOrWhiteSpace(apiEnvironment))
                sql = ApiEnvLine.Replace(sql, m => m.Groups[1].Value + apiEnvironment.Trim().Replace("'", "''") + m.Groups[2].Value, 1);
            return sql;
        }

        public const string ExpectedVersion = "3";

        private static readonly Regex GoLine = new(@"^\s*GO\s*(?:--.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string ReadScript(string fileName)
        {
            var asm = typeof(SqlServerSchema).Assembly;
            using var s = asm.GetManifestResourceStream("POGov.SqlServer." + fileName)
                          ?? throw new InvalidOperationException("Embedded script missing: " + fileName);
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }

        /// <summary>The batches of a script (text between GO lines), empty ones dropped.</summary>
        public static List<string> SplitBatches(string script) =>
            GoLine.Split(script).Select(b => b.Trim()).Where(b => b.Length > 0 && !IsOnlyComments(b)).ToList();

        private static bool IsOnlyComments(string batch)
        {
            var noBlock = Regex.Replace(batch, @"/\*.*?\*/", "", RegexOptions.Singleline);
            var noLine = Regex.Replace(noBlock, @"--[^\n]*", "");
            return noLine.Trim().Length == 0;
        }

        /// <summary>Runs 01..05 against the database of <paramref name="connectionString"/> (the API_MASTER seed with <paramref name="apiBaseUrl"/> when given).</summary>
        public static Task ApplyAsync(string connectionString, Action<string>? say = null, CancellationToken ct = default) =>
            ApplyAsync(connectionString, DatabaseScripts, say, null, ct);

        /// <summary>Runs the given scripts (in order) against the database of <paramref name="connectionString"/>.</summary>
        public static async Task ApplyAsync(string connectionString, IEnumerable<string> scripts, Action<string>? say = null, string? apiBaseUrl = null,
                                            CancellationToken ct = default, string? apiEnvironment = null)
        {
            await using var c = new SqlConnection(connectionString);
            await c.OpenAsync(ct);
            foreach (var file in scripts)
            {
                var batches = SplitBatches(file == ApiMasterScript ? ApiMasterSql(apiBaseUrl, apiEnvironment) : ReadScript(file));
                foreach (var b in batches)
                    await c.ExecuteAsync(new CommandDefinition(b, commandTimeout: 300, cancellationToken: ct));
                say?.Invoke($"  {file}: {batches.Count} batches");
            }
        }

        /// <summary>
        /// CREATE DATABASE (when missing) + READ_COMMITTED_SNAPSHOT, through the master database of the same
        /// server — what 00_create_database.sql does, for any database name.
        /// </summary>
        public static async Task CreateDatabaseAsync(string connectionString, CancellationToken ct = default)
        {
            var b = new SqlConnectionStringBuilder(connectionString);
            var db = b.InitialCatalog;
            if (string.IsNullOrWhiteSpace(db)) throw new ArgumentException("The connection string names no database (Initial Catalog / Database).");
            b.InitialCatalog = "master";
            b.Pooling = false;
            await using var c = new SqlConnection(b.ConnectionString);
            await c.OpenAsync(ct);
            await c.ExecuteAsync(new CommandDefinition(
                "DECLARE @s NVARCHAR(MAX); " +
                "IF DB_ID(@db) IS NULL BEGIN SET @s = N'CREATE DATABASE ' + QUOTENAME(@db); EXEC(@s); END; " +
                "IF EXISTS (SELECT 1 FROM sys.databases WHERE name = @db AND is_read_committed_snapshot_on = 0) " +
                "BEGIN SET @s = N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE'; EXEC(@s); END;",
                new { db }, commandTimeout: 300, cancellationToken: ct));
        }

        /// <summary>DROP DATABASE (tests only).</summary>
        public static async Task DropDatabaseAsync(string connectionString, CancellationToken ct = default)
        {
            var b = new SqlConnectionStringBuilder(connectionString);
            var db = b.InitialCatalog;
            SqlConnection.ClearAllPools();
            b.InitialCatalog = "master";
            b.Pooling = false;
            await using var c = new SqlConnection(b.ConnectionString);
            await c.OpenAsync(ct);
            await c.ExecuteAsync(new CommandDefinition(
                "DECLARE @s NVARCHAR(MAX); " +
                "IF DB_ID(@db) IS NOT NULL BEGIN SET @s = N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE'; EXEC(@s); " +
                "SET @s = N'DROP DATABASE ' + QUOTENAME(@db); EXEC(@s); END", new { db }, commandTimeout: 300, cancellationToken: ct));
        }
    }
}

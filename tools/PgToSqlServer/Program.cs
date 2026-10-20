using System.Diagnostics;
using Application.Interfaces.V1;
using Dapper;
using Infrastructure.Repositories;
using Infrastructure.Utilitys;
using Npgsql;

// PgToSqlServer — copies a v1.1.4 / .NET PostgreSQL database (schema v2) into SQL Server (schema v2 + PRO_POGOV_*
// procedures) with the logic of storage.py copy_store, then compares both sides (verify_copy).
// See database/sqlserver/README.md ("Migrating from the .37 PostgreSQL").

var opts = Options.Parse(args);
if (opts == null) return 2;

var tables = new[]
{
    "app_settings", "app_users", "app_sessions", "doc_stamps", "upload_log", "upload_batches", "run_index",
    "request_events", "audit_log", "dataset_versions", "run_files", "feedback_log"
};

try
{
    var sqlCs = opts.EncryptedPassword ? DapperHelper.DecodePassword(opts.Sql) : opts.Sql;
    if (opts.CreateDatabase)
    {
        Console.WriteLine("Creating the SQL Server database (if missing) ...");
        await SqlServerSchema.CreateDatabaseAsync(sqlCs);
    }
    using var sql = new SqlServerStore(sqlCs, createSchema: opts.CreateSchema);
    using var pg = new PgStore(opts.Pg);
    Console.WriteLine("PostgreSQL : " + pg.Describe());
    Console.WriteLine("SQL Server : " + sql.Describe());
    await sql.InitializeAsync(0);

    IStore src = opts.Reverse ? sql : pg;
    IStore dst = opts.Reverse ? pg : sql;
    Console.WriteLine(opts.Reverse ? "Direction  : SQL Server -> PostgreSQL (rollback copy)" : "Direction  : PostgreSQL -> SQL Server");
    if (opts.Reverse) await pg.InitializeAsync(0);

    async Task<Dictionary<string, long>> PgCounts()
    {
        await using var c = new NpgsqlConnection(opts.Pg);
        await c.OpenAsync();
        var d = new Dictionary<string, long>();
        foreach (var t in tables) d[t] = await c.ExecuteScalarAsync<long>($"SELECT count(*) FROM {t}");
        return d;
    }

    async Task<Dictionary<string, long>> SqlCounts()
    {
        var d = new Dictionary<string, long>();
        foreach (var t in tables) d[t] = await sql!.Db.QuerySingleAsync<long>($"SELECT COUNT_BIG(*) FROM dbo.{t}");
        return d;
    }

    Task<Dictionary<string, long>> SrcCounts() => opts.Reverse ? SqlCounts() : PgCounts();
    Task<Dictionary<string, long>> DstCounts() => opts.Reverse ? PgCounts() : SqlCounts();

    var sw = Stopwatch.StartNew();
    if (!opts.VerifyOnly)
    {
        var before = await DstCounts();
        var nonEmpty = before.Where(kv => kv.Value > 0).ToList();
        if (nonEmpty.Count > 0)
        {
            if (!opts.Force)
            {
                Console.Error.WriteLine("The target is not empty (" + string.Join(", ", nonEmpty.Select(kv => kv.Key + " " + kv.Value)) +
                                        "). Nothing was copied. Use --force to empty it first, or --verify-only to compare.");
                return 2;
            }
            Console.WriteLine("--force: emptying the target ...");
            if (dst is SqlServerStore s) await s.TruncateAllAsync(); else await ((PgStore)dst).TruncateAllAsync();
        }

        Console.WriteLine("Copying ...");
        // keep API instances that use the target off it while the copy runs (SQL Server: cross-process lock)
        using (await dst.LockAsync())
        {
            var counts = await StoreCopy.CopyAsync(src, dst, Console.WriteLine);
            Console.WriteLine("Copied: " + string.Join(", ", counts.Select(kv => kv.Key + "=" + kv.Value)));
        }
        Console.WriteLine($"Copy finished in {sw.Elapsed.TotalSeconds:0.0} s");
    }

    Console.WriteLine("Row counts (table: source -> target):");
    var sc = await SrcCounts();
    var dc = await DstCounts();
    foreach (var t in tables)
        Console.WriteLine($"  {t,-17} {sc[t],8} -> {dc[t],8}{(sc[t] == dc[t] ? "" : "   <- differs")}");

    Console.WriteLine("Verifying (verify_copy) ...");
    var diffs = await StoreCopy.VerifyAsync(src, dst);
    if (diffs.Count == 0)
    {
        Console.WriteLine($"OK - no differences ({sw.Elapsed.TotalSeconds:0.0} s).");
        return 0;
    }
    Console.WriteLine($"{diffs.Count} difference(s):");
    foreach (var d in diffs) Console.WriteLine("  " + d);
    return 1;
}
catch (Exception e)
{
    Console.Error.WriteLine("Failed: " + e.Message);
    return 3;
}

internal sealed class Options
{
    public string Pg = "";
    public string Sql = "";
    public bool EncryptedPassword, CreateDatabase, CreateSchema, Force, VerifyOnly, Reverse;

    private const string Usage = @"PgToSqlServer - copy the PO Governance PostgreSQL database into SQL Server and verify it.

  PgToSqlServer --pg ""Host=127.0.0.1;Port=5433;Database=pogov;Username=vmm_app;Password=..."" \
                --sql ""Server=.;Database=POGovernance;User Id=vmm_app;Password=...;TrustServerCertificate=True""

  --pg <conn>            PostgreSQL source (or env POGOV_PG)
  --sql <conn>           SQL Server target (or env POGOV_SQL)
  --encrypted-password   the --sql password is My_Encode()d (as in appsettings with EncryptedDbPassword = true)
  --create-database      create the SQL Server database first (needs CREATE DATABASE permission)
  --create-schema        run database/sqlserver/01..03 when the schema is missing
  --force                empty a non-empty target first (otherwise the tool refuses to copy)
  --verify-only          do not copy, only compare both sides
  --reverse              copy SQL Server -> PostgreSQL (rollback)
Exit code: 0 identical, 1 differences, 2 refused / usage, 3 error.";

    public static Options? Parse(string[] args)
    {
        var o = new Options
        {
            Pg = Environment.GetEnvironmentVariable("POGOV_PG") ?? "",
            Sql = Environment.GetEnvironmentVariable("POGOV_SQL") ?? ""
        };
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pg" when i + 1 < args.Length: o.Pg = args[++i]; break;
                case "--sql" when i + 1 < args.Length: o.Sql = args[++i]; break;
                case "--encrypted-password": o.EncryptedPassword = true; break;
                case "--create-database": o.CreateDatabase = true; break;
                case "--create-schema": o.CreateSchema = true; break;
                case "--force": o.Force = true; break;
                case "--verify-only": o.VerifyOnly = true; break;
                case "--reverse": o.Reverse = true; break;
                case "-h" or "--help": Console.WriteLine(Usage); return null;
                default:
                    Console.Error.WriteLine("Unknown argument " + args[i] + "\n\n" + Usage);
                    return null;
            }
        }
        if (string.IsNullOrWhiteSpace(o.Pg) || string.IsNullOrWhiteSpace(o.Sql))
        {
            Console.Error.WriteLine("Both --pg and --sql are required.\n\n" + Usage);
            return null;
        }
        return o;
    }
}

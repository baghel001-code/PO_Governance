using Infrastructure.Repositories;
using Infrastructure.Utilitys;
using Microsoft.Data.SqlClient;

namespace POGovernance.Tests.SqlServer
{
    /// <summary>
    /// SQL Server integration tests run only when the environment variable POGOV_SQLSERVER_TEST holds a
    /// connection string (any database name: each test class gets its own fresh database
    /// POGovTest_&lt;random&gt;, created with database/sqlserver/01..03 and dropped afterwards), e.g.
    /// <c>Server=localhost,1433;User Id=sa;Password=…;TrustServerCertificate=True</c>.
    /// </summary>
    public static class SqlServerTestEnv
    {
        public const string Variable = "POGOV_SQLSERVER_TEST";
        public static string? ConnectionString => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } s ? s : null;
    }

    /// <summary>[Fact] that is skipped when POGOV_SQLSERVER_TEST is not set.</summary>
    public sealed class SqlServerFactAttribute : FactAttribute
    {
        public SqlServerFactAttribute()
        {
            if (SqlServerTestEnv.ConnectionString == null) Skip = $"set {SqlServerTestEnv.Variable} to run the SQL Server tests";
        }
    }

    public sealed class SqlServerFixture : IAsyncLifetime
    {
        public string ConnectionString { get; private set; } = "";

        public async Task InitializeAsync()
        {
            if (SqlServerTestEnv.ConnectionString is not { } cs) return;
            var b = new SqlConnectionStringBuilder(cs) { InitialCatalog = "POGovTest_" + Guid.NewGuid().ToString("N")[..10] };
            ConnectionString = b.ConnectionString;
            await SqlServerSchema.CreateDatabaseAsync(ConnectionString);
            await SqlServerSchema.ApplyAsync(ConnectionString);
        }

        public SqlServerStore NewStore() => new(ConnectionString);

        public async Task DisposeAsync()
        {
            if (ConnectionString.Length > 0) await SqlServerSchema.DropDatabaseAsync(ConnectionString);
        }
    }
}

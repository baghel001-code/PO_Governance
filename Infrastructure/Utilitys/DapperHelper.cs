using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyNewEncDec;

namespace Infrastructure.Utilitys
{
    /// <summary>
    /// Dapper helper for SQL Server, in the QPS style (same class and method names as
    /// QPS <c>Infrastructure/Utilitys/DapperHelper.cs</c>): the connection string is
    /// <c>ConnectionStrings:DefaultConnection</c>; when <c>PoGovSettings:EncryptedDbPassword</c> is true its
    /// password is decoded with <c>New_Enc_Dec.My_Decode</c> (Dlls\MyNewEncDec.dll, as in QPS).
    /// <para>
    /// Differences from QPS: Microsoft.Data.SqlClient instead of the deprecated System.Data.SqlClient (same API);
    /// every method takes an optional <see cref="CancellationToken"/> and command timeout; and errors are
    /// never swallowed (QPS's ExecuteStoredProcedureListAsync returned null on an exception) — the caller
    /// (SqlServerStore) needs to see them to retry or to report "database not reachable".
    /// </para>
    /// </summary>
    public class DapperHelper
    {
        public const string ConnectionStringName = "DefaultConnection";
        public const string EncryptedPasswordKey = "PoGovSettings:EncryptedDbPassword";

        private readonly string _connectionString;
        private readonly ILogger<DapperHelper>? _logger;

        /// <summary>QPS constructor: ConnectionStrings:DefaultConnection (+ PoGovSettings:ConnectionString as fallback).</summary>
        public DapperHelper(IConfiguration configuration, New_Enc_Dec Enc_Dec, ILogger<DapperHelper> logger)
            : this(BuildConnectionString(configuration, Enc_Dec), logger)
        {
        }

        /// <summary>A ready (already decoded) connection string — tests and the migration tool.</summary>
        public DapperHelper(string connectionString, ILogger<DapperHelper>? logger = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("No SQL Server connection string: set ConnectionStrings:" + ConnectionStringName + ".");
            _connectionString = Normalize(connectionString);
            _logger = logger;
        }

        /// <summary>
        /// Reads ConnectionStrings:DefaultConnection (or PoGovSettings:ConnectionString) and decodes the password
        /// with <c>My_Decode</c> when PoGovSettings:EncryptedDbPassword is true.
        /// </summary>
        public static string BuildConnectionString(IConfiguration configuration) => BuildConnectionString(configuration, null);

        /// <inheritdoc cref="BuildConnectionString(IConfiguration)"/>
        public static string BuildConnectionString(IConfiguration configuration, New_Enc_Dec? encDec)
        {
            var cs = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(cs)) cs = configuration["PoGovSettings:ConnectionString"];
            if (string.IsNullOrWhiteSpace(cs))
                throw new InvalidOperationException("No SQL Server connection string: set ConnectionStrings:" + ConnectionStringName + ".");
            var encrypted = configuration.GetValue<bool>(EncryptedPasswordKey);
            return DecodePassword(cs, encrypted ? encDec ?? new New_Enc_Dec() : null);
        }

        /// <summary>Replaces the Password of <paramref name="connectionString"/> by My_Decode(Password) (QPS).</summary>
        public static string DecodePassword(string connectionString, New_Enc_Dec? encDec)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (encDec != null && !string.IsNullOrEmpty(builder.Password))
                builder.Password = encDec.My_Decode(builder.Password);
            return builder.ConnectionString;
        }

        /// <summary>DecodePassword with a new <see cref="New_Enc_Dec"/> (callers that do not reference MyNewEncDec.dll).</summary>
        public static string DecodePassword(string connectionString) => DecodePassword(connectionString, new New_Enc_Dec());

        /// <summary>My_Encode of a plain password — what goes into the connection string when EncryptedDbPassword is true.</summary>
        public static string EncodePassword(string plainPassword) => new New_Enc_Dec().My_Encode(plainPassword);

        /// <summary>Application Name and a 60 s command timeout (PgStore) unless the connection string sets them.</summary>
        private static string Normalize(string connectionString)
        {
            var b = new SqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrEmpty(b.ApplicationName) || b.ApplicationName == "Core Microsoft SqlClient Data Provider")
                b.ApplicationName = "vmm-po-governance";
            if (b.CommandTimeout == 30) b.CommandTimeout = 60;
            return b.ConnectionString;
        }

        /// <summary>The connection string (password decoded) — for diagnostics use <see cref="Describe"/>.</summary>
        public string ConnectionString => _connectionString;

        /// <summary>"user@server/database" without the password.</summary>
        public string Describe()
        {
            var b = new SqlConnectionStringBuilder(_connectionString);
            var who = b.IntegratedSecurity ? "integrated" : b.UserID;
            return $"{who}@{b.DataSource}/{b.InitialCatalog}";
        }

        public int CommandTimeout => new SqlConnectionStringBuilder(_connectionString).CommandTimeout;

        // Open a database connection
        public SqlConnection GetConnection() => new(_connectionString);

        /// <summary>A connection outside the pool: closing it ends the SQL Server session (used for the application lock).</summary>
        public SqlConnection GetDedicatedConnection(string purpose)
        {
            var b = new SqlConnectionStringBuilder(_connectionString) { Pooling = false };
            b.ApplicationName = (b.ApplicationName + " (" + purpose + ")").Length <= 128 ? b.ApplicationName + " (" + purpose + ")" : b.ApplicationName;
            return new SqlConnection(b.ConnectionString);
        }

        /// <summary>Empties the connection pool (after the server was restarted, every pooled connection is dead).</summary>
        public void ClearPool()
        {
            using var c = GetConnection();
            SqlConnection.ClearPool(c);
        }

        private static CommandDefinition Cmd(string sql, object? param, CommandType commandType, int? commandTimeout, CancellationToken ct) =>
            new(sql, param, commandType: commandType, commandTimeout: commandTimeout, cancellationToken: ct);

        // Query a single entity from the database
        public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null, CommandType commandType = CommandType.StoredProcedure,
            int? commandTimeout = null, CancellationToken ct = default)
        {
            await using var connection = GetConnection();
            await OpenConnectionAsync(connection, ct);
            return await connection.QueryFirstOrDefaultAsync<T>(Cmd(sql, param, commandType, commandTimeout, ct));
        }

        // Query a single entity from the database (text command)
        public async Task<T?> QuerySingleAsync<T>(string sql, object? param = null, CancellationToken ct = default)
        {
            await using var connection = GetConnection();
            await OpenConnectionAsync(connection, ct);
            return await connection.QuerySingleOrDefaultAsync<T>(Cmd(sql, param, CommandType.Text, null, ct));
        }

        // Query a list of entities from the database (text command)
        public async Task<IEnumerable<T>> QueryListAsync<T>(string sql, object? param = null, CancellationToken ct = default)
        {
            await using var connection = GetConnection();
            await OpenConnectionAsync(connection, ct);
            return await connection.QueryAsync<T>(Cmd(sql, param, CommandType.Text, null, ct));
        }

        // Execute a command (e.g., Insert, Update, Delete) (text command)
        public async Task<int> ExecuteAsync(string sql, object? param = null, int? commandTimeout = null, CancellationToken ct = default)
        {
            await using var connection = GetConnection();
            await OpenConnectionAsync(connection, ct);
            return await connection.ExecuteAsync(Cmd(sql, param, CommandType.Text, commandTimeout, ct));
        }

        // Execute the stored procedure (non-query operation) and return the number of rows affected
        public async Task<int> ExecuteStoredProcedureAsync(string storedProc, object? parameters = null, int? commandTimeout = null, CancellationToken ct = default)
        {
            await using var connection = GetConnection();
            await OpenConnectionAsync(connection, ct);
            return await connection.ExecuteAsync(Cmd(storedProc, parameters, CommandType.StoredProcedure, commandTimeout, ct));
        }

        // Execute a stored procedure with an Output Parameter
        public async Task<string?> ExecuteStoredProcedureAsync(string storedProc, object? inputParam, string outputParamName, CancellationToken ct = default)
        {
            await using var connection = GetConnection();
            var parameters = new DynamicParameters(inputParam);
            parameters.Add(outputParamName, dbType: DbType.String, direction: ParameterDirection.Output, size: 500);
            await OpenConnectionAsync(connection, ct);
            await connection.ExecuteAsync(Cmd(storedProc, parameters, CommandType.StoredProcedure, null, ct));
            return parameters.Get<string?>(outputParamName);
        }

        public async Task<T?> ExecuteScalerStoredProcedureAsync<T>(string storedProc, object? param = null, CancellationToken ct = default)
        {
            await using var connection = GetConnection();
            await OpenConnectionAsync(connection, ct);
            return await connection.ExecuteScalarAsync<T>(Cmd(storedProc, param, CommandType.StoredProcedure, null, ct));
        }

        // Execute a stored procedure Single
        public async Task<T?> ExecuteStoredProcedureSingleAsync<T>(string storedProc, object? param = null, int? commandTimeout = null, CancellationToken ct = default)
        {
            await using var connection = GetConnection();
            await OpenConnectionAsync(connection, ct);
            return await connection.QueryFirstOrDefaultAsync<T>(Cmd(storedProc, param, CommandType.StoredProcedure, commandTimeout, ct));
        }

        // Execute a stored procedure Single and an output parameter
        public async Task<(T?, string?)> ExecuteStoredProcedureSingleAsync<T>(string storedProc, object? inputParams, string outputParamName, CancellationToken ct = default)
        {
            await using var connection = GetConnection();
            var parameters = new DynamicParameters(inputParams);
            parameters.Add(outputParamName, dbType: DbType.String, direction: ParameterDirection.Output, size: 500);
            await OpenConnectionAsync(connection, ct);
            var resultSet = await connection.QueryFirstOrDefaultAsync<T>(Cmd(storedProc, parameters, CommandType.StoredProcedure, null, ct));
            return (resultSet, parameters.Get<string?>(outputParamName));
        }

        // Execute a stored procedure (list)
        public async Task<IEnumerable<T>> ExecuteStoredProcedureListAsync<T>(string storedProc, object? param = null, int? commandTimeout = null, CancellationToken ct = default)
        {
            await using var connection = GetConnection();
            await OpenConnectionAsync(connection, ct);
            try
            {
                return await connection.QueryAsync<T>(Cmd(storedProc, param, CommandType.StoredProcedure, commandTimeout, ct));
            }
            catch (Exception ex) when (LogError(ex, storedProc))
            {
                throw;   // unreachable: LogError returns false (log without unwinding)
            }
        }

        public async Task<(IEnumerable<T> Data, int TotalCount)> ExecuteStoredProcedureListDPAsync<T>(string storedProc, DynamicParameters param, CancellationToken ct = default)
        {
            await using var connection = GetConnection();
            await OpenConnectionAsync(connection, ct);
            var res = await connection.QueryAsync<T>(Cmd(storedProc, param, CommandType.StoredProcedure, null, ct));
            return (res, param.Get<int>("@TotalCount"));
        }

        // Execute stored procedure with a single result set and an output parameter
        public async Task<(IEnumerable<T>, string?)> ExecuteStoredProcedureListAsync<T>(string storedProc, object? inputParams, string outputParamName, CancellationToken ct = default)
        {
            await using var connection = GetConnection();
            var parameters = new DynamicParameters(inputParams);
            parameters.Add(outputParamName, dbType: DbType.String, direction: ParameterDirection.Output, size: 500);
            await OpenConnectionAsync(connection, ct);
            var resultSet = await connection.QueryAsync<T>(Cmd(storedProc, parameters, CommandType.StoredProcedure, null, ct));
            return (resultSet, parameters.Get<string?>(outputParamName));
        }

        // Execute a stored procedure Multiple
        public async Task<(IEnumerable<T1>, IEnumerable<T2>)> ExecuteStoredProcedureMultipleListAsync<T1, T2>(string storedProc, object? param = null, CancellationToken ct = default)
        {
            await using var connection = GetConnection();
            await OpenConnectionAsync(connection, ct);
            using var multi = await connection.QueryMultipleAsync(Cmd(storedProc, param, CommandType.StoredProcedure, null, ct));
            var result1 = await multi.ReadAsync<T1>();
            var result2 = await multi.ReadAsync<T2>();
            return (result1, result2);
        }

        public async Task<(IEnumerable<T1>, IEnumerable<T2>, IEnumerable<T3>)> ExecuteStoredProcedureMultiple3ListAsync<T1, T2, T3>(string storedProc, object? param = null, CancellationToken ct = default)
        {
            await using var connection = GetConnection();
            await OpenConnectionAsync(connection, ct);
            using var multi = await connection.QueryMultipleAsync(Cmd(storedProc, param, CommandType.StoredProcedure, null, ct));
            var result1 = await TryReadAsync<T1>(multi);
            var result2 = await TryReadAsync<T2>(multi);
            var result3 = await TryReadAsync<T3>(multi);
            return (result1, result2, result3);
        }

        // Execute stored procedure with multiple result sets and output parameters
        public async Task<(IEnumerable<T1>, IEnumerable<T2>, string?)> ExecuteStoredProcedureMultipleListAsync<T1, T2>(string storedProc, object? inputParams, string outputParamName, CancellationToken ct = default)
        {
            await using var connection = GetConnection();
            var parameters = new DynamicParameters(inputParams);
            parameters.Add(outputParamName, dbType: DbType.String, direction: ParameterDirection.Output, size: 500);
            await OpenConnectionAsync(connection, ct);
            using var multi = await connection.QueryMultipleAsync(Cmd(storedProc, parameters, CommandType.StoredProcedure, null, ct));
            var result1 = await multi.ReadAsync<T1>();
            var result2 = await multi.ReadAsync<T2>();
            return (result1, result2, parameters.Get<string?>(outputParamName));
        }

        // Execute a stored procedure N Multiple
        public async Task<IEnumerable<dynamic>[]> ExecuteStoredProcedureWithNListAsync(string storedProc, object? param = null, CancellationToken ct = default)
        {
            await using var connection = GetConnection();
            await OpenConnectionAsync(connection, ct);
            using var multi = await connection.QueryMultipleAsync(Cmd(storedProc, param, CommandType.StoredProcedure, null, ct));
            var resultSets = new List<IEnumerable<dynamic>>();
            while (!multi.IsConsumed) resultSets.Add(await multi.ReadAsync<dynamic>());
            return resultSets.ToArray();
        }

        // Execute stored procedure with N result sets and an output parameter
        public async Task<(IEnumerable<dynamic>[] resultSets, string? outputValue)> ExecuteStoredProcedureWithNListAsync(string storedProc, object? inputParams, string outputParamName, CancellationToken ct = default)
        {
            await using var connection = GetConnection();
            var parameters = new DynamicParameters(inputParams);
            parameters.Add(outputParamName, dbType: DbType.String, direction: ParameterDirection.Output, size: 500);
            await OpenConnectionAsync(connection, ct);
            using var multi = await connection.QueryMultipleAsync(Cmd(storedProc, parameters, CommandType.StoredProcedure, null, ct));
            var resultSets = new List<IEnumerable<dynamic>>();
            while (!multi.IsConsumed) resultSets.Add(await multi.ReadAsync<dynamic>());
            return (resultSets.ToArray(), parameters.Get<string?>(outputParamName));
        }

        // ── QPS output-parameter convention: @Error_Code INT / @Error_Message NVARCHAR(500) ──

        /// <summary>Adds @Error_Code / @Error_Message output parameters (QPS services do this for every PRO_* call).</summary>
        public static DynamicParameters AddErrorOutputs(DynamicParameters parameters)
        {
            parameters.Add("@Error_Code", dbType: DbType.Int32, direction: ParameterDirection.Output);
            parameters.Add("@Error_Message", dbType: DbType.String, size: 500, direction: ParameterDirection.Output);
            return parameters;
        }

        public static int ErrorCode(DynamicParameters parameters) => parameters.Get<int?>("@Error_Code") ?? 0;

        public static string ErrorMessage(DynamicParameters parameters) => parameters.Get<string?>("@Error_Message") ?? "";

        // Helper method to open connection asynchronously
        private static Task OpenConnectionAsync(SqlConnection connection, CancellationToken ct) => connection.OpenAsync(ct);

        private bool LogError(Exception ex, string storedProc)
        {
            _logger?.LogError(ex, "SP {Sp} failed.", storedProc);
            return false;
        }

        // Helper method to safely try reading a result set
        private static async Task<IEnumerable<T>> TryReadAsync<T>(SqlMapper.GridReader multi)
        {
            try
            {
                var result = await multi.ReadAsync<T>();
                return result ?? Enumerable.Empty<T>();
            }
            catch (InvalidOperationException)
            {
                return Enumerable.Empty<T>();
            }
        }
    }
}

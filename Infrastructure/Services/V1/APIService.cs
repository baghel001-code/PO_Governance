using System.Data;
using System.Text.RegularExpressions;
using Application.Interfaces.V1;
using Dapper;
using Domain.Entities.Common;
using Domain.Entities.Response;
using Infrastructure.Utilitys;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.V1
{
    /// <summary>
    /// QPS APIService: the API master list (API_MASTER) through <c>PRO_GET_ALL_API</c> (database/sqlserver/04_api_master.sql).
    /// The URLs are those of <see cref="Environment"/> (API_BASE_URL base URL + API_MASTER path, resolved by the procedure).
    /// With <c>PoGovSettings:Storage = postgres</c> there is no SQL Server: the list is then built from the seed rows of the
    /// embedded 04_api_master.sql, with <c>ApiMaster:BaseUrl</c> as the base URL and <c>JwtSettings:SecretKey</c> as the token.
    /// </summary>
    public sealed class APIService : IAPI
    {
        private static readonly Regex SeedRow = new(
            @"\(N'(?<api>[^']+)',\s*N'(?<task>[^']+)',\s*N'(?<action>[^']+)',\s*N'(?<method>[^']+)',\s*N'(?<ct>[^']+)',\s*(?<to>\d+),\s*N'(?<desc>(?:[^']|'')*)'\)",
            RegexOptions.Compiled);

        private readonly DapperHelper? _db;
        private readonly IConfiguration _cfg;
        private readonly ILogger<APIService>? _log;

        public APIService(DapperHelper? db, IConfiguration cfg, ILogger<APIService>? log = null, string? environment = null)
        {
            _db = db;
            _cfg = cfg;
            _log = log;
            Environment = string.IsNullOrWhiteSpace(environment) ? "Production" : environment.Trim();
        }

        /// <summary>Development / Staging / Production: the API_BASE_URL rows used for the URLs.</summary>
        public string Environment { get; }

        public async Task<ApiMasterRes> GetAllApi(CancellationToken ct = default)
        {
            if (_db == null) return FromSeed(null);
            var p = DapperHelper.AddErrorOutputs(new DynamicParameters());
            p.Add("@Cmd", "ALL", DbType.String);
            p.Add("@Environment", Environment, DbType.String);
            return await RunAsync(p, ct);
        }

        public async Task<ApiMasterRes> GetSpecificApiData(string? taskName, string? param, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskName)) return new ApiMasterRes { responseCode = 400, responseMessage = "TaskName is required" };
            if (_db == null) return FromSeed(taskName);
            var p = DapperHelper.AddErrorOutputs(new DynamicParameters());
            p.Add("@Cmd", "BY_TASK", DbType.String);
            p.Add("@Task_Name", taskName, DbType.String);
            p.Add("@Parm", param ?? "", DbType.String);
            p.Add("@Environment", Environment, DbType.String);
            return await RunAsync(p, ct);
        }

        public async Task<ApiMasterRes> GetServerApiData(string taskName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskName)) return new ApiMasterRes { responseCode = 400, responseMessage = "TaskName is required" };
            if (_db == null)
                return new ApiMasterRes { responseCode = 404, responseMessage = $"{taskName}: server-only APIs (HRMS) need the SQL Server API master" };
            var p = DapperHelper.AddErrorOutputs(new DynamicParameters());
            p.Add("@Cmd", "SERVER_TASK", DbType.String);
            p.Add("@Task_Name", taskName, DbType.String);
            p.Add("@Environment", Environment, DbType.String);
            return await RunAsync(p, ct);
        }

        public async Task<string?> GetBaseUrlAsync(string baseUrlKey, CancellationToken ct = default)
        {
            if (_db == null || string.IsNullOrWhiteSpace(baseUrlKey)) return null;
            var p = DapperHelper.AddErrorOutputs(new DynamicParameters());
            p.Add("@Cmd", "BASE_URL", DbType.String);
            p.Add("@Parm", baseUrlKey.Trim(), DbType.String);
            p.Add("@Environment", Environment, DbType.String);
            var res = await RunAsync(p, ct);
            var url = res.responseCode == 0 ? res.ApiData?.FirstOrDefault()?.Api_Url : null;
            return string.IsNullOrWhiteSpace(url) ? null : url.EndsWith('/') ? url : url + "/";
        }

        private async Task<ApiMasterRes> RunAsync(DynamicParameters p, CancellationToken ct)
        {
            List<ApiData> rows;
            try
            {
                rows = (await _db!.ExecuteStoredProcedureListAsync<ApiData>("PRO_GET_ALL_API", p, commandTimeout: null, ct: ct)).ToList();
            }
            catch (SqlException e) when (e.Number is 2812 or 8144)   // procedure missing, or the old one without @Environment
            {
                _log?.LogError("PRO_GET_ALL_API is missing: run database/sqlserver/04_api_master.sql (or set PoGovSettings:SqlServerCreateSchema = true).");
                return new ApiMasterRes { responseCode = 1, responseMessage = "The API master is not installed: run database/sqlserver/04_api_master.sql" };
            }
            var code = DapperHelper.ErrorCode(p);
            if (code == 50424) _log?.LogError("API master: {Message}", DapperHelper.ErrorMessage(p));
            if (code != 0) return new ApiMasterRes { responseCode = code == 50404 ? 404 : code, responseMessage = DapperHelper.ErrorMessage(p), ApiData = rows };
            if (rows.Count == 0) return new ApiMasterRes { responseCode = 1, responseMessage = "No data found", ApiData = rows };
            return new ApiMasterRes { ApiData = rows };
        }

        /// <summary>The 04_api_master.sql seed rows (no SQL Server: PostgreSQL storage).</summary>
        private ApiMasterRes FromSeed(string? taskName)
        {
            var baseUrl = _cfg["ApiMaster:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "http://localhost:5080/";
            if (!baseUrl.EndsWith('/')) baseUrl += "/";
            var rows = ParseSeed(SqlServerSchema.ReadScript("04_api_master.sql"), baseUrl, _cfg["JwtSettings:SecretKey"]);
            if (taskName != null) rows = rows.Where(r => r.Task_Name == taskName).ToList();
            return rows.Count == 0 ? new ApiMasterRes { responseCode = taskName == null ? 1 : 404, responseMessage = "No data found", ApiData = rows }
                                   : new ApiMasterRes { ApiData = rows };
        }

        /// <summary>The seed VALUES of 04_api_master.sql as rows (also used by the tests).</summary>
        public static List<ApiData> ParseSeed(string script, string baseUrl, string? encodedToken) =>
            SeedRow.Matches(script).Select(m => new ApiData
            {
                Api_Name = m.Groups["api"].Value,
                Task_Name = m.Groups["task"].Value,
                Api_Url = baseUrl + "api/v1/" + m.Groups["action"].Value,
                Api_Http_Method = m.Groups["method"].Value,
                Api_Content_Type = m.Groups["ct"].Value,
                Api_Timeout = int.Parse(m.Groups["to"].Value),
                Api_Authorization_Type = "Bearer",
                Api_Authorization = encodedToken,
                Active_Status = true
            }).ToList();
    }
}

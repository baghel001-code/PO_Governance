using Dapper;
using Infrastructure.Services.V1;
using Infrastructure.Utilitys;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace POGovernance.Tests.SqlServer
{
    /// <summary>04_api_master.sql against a real server: API_MASTER seed, PRO_GET_ALL_API (ALL / BY_TASK) through APIService.</summary>
    public class ApiMasterSqlServerTests : IClassFixture<SqlServerFixture>
    {
        private readonly SqlServerFixture _fx;
        public ApiMasterSqlServerTests(SqlServerFixture fx) => _fx = fx;

        private APIService Service() => new(new DapperHelper(_fx.ConnectionString), new ConfigurationBuilder().Build());

        [SqlServerFact]
        public async Task GetAllApi_returns_every_seed_row_and_GetSpecificApi_one()
        {
            var all = await Service().GetAllApi();
            Assert.Equal(0, all.responseCode);
            var seed = APIService.ParseSeed(SqlServerSchema.ReadScript(SqlServerSchema.ApiMasterScript), "http://localhost:5080/", null);
            Assert.Equal(seed.Select(r => r.Task_Name).OrderBy(x => x), all.ApiData!.Select(r => r.Task_Name).OrderBy(x => x));
            var v = all.ApiData!.Single(r => r.Task_Name == "PO_TOOL1_VALIDATE");
            Assert.Equal("http://localhost:5080/api/v1/Tool1Validate", v.Api_Url);
            Assert.Equal("POST", v.Api_Http_Method);
            Assert.Equal("application/json", v.Api_Content_Type);
            Assert.Equal("Bearer", v.Api_Authorization_Type);
            Assert.Equal("PoGov-Static-Dev-Token-2026-Change-Me", Infrastructure.Middleware.StaticTokenMiddleware.DecodeToken(v.Api_Authorization!));
            Assert.True(v.Active_Status);
            Assert.Equal("GET", all.ApiData!.Single(r => r.Task_Name == "PO_GET_ALL_API").Api_Http_Method);
            Assert.Equal("multipart/form-data", all.ApiData!.Single(r => r.Task_Name == "PO_INGEST_ZIP").Api_Content_Type);

            var one = await Service().GetSpecificApiData("PO_GRC_COMPUTE", null);
            Assert.Equal(0, one.responseCode);
            Assert.Equal("http://localhost:5080/api/v1/GrcCompute", Assert.Single(one.ApiData!).Api_Url);
            var none = await Service().GetSpecificApiData("PO_NOPE", null);
            Assert.Equal(404, none.responseCode);
        }

        [SqlServerFact]
        public async Task Rerunning_the_script_keeps_changed_urls_and_inactive_rows_are_hidden()
        {
            await using (var c = new SqlConnection(_fx.ConnectionString))
            {
                await c.ExecuteAsync("UPDATE dbo.API_MASTER SET Api_Url = REPLACE(Api_Url, N'http://localhost:5080/', N'https://api.example/') WHERE Task_Name = N'PO_HELP_PARAMS'");
                await c.ExecuteAsync("UPDATE dbo.API_MASTER SET Active_Status = 0 WHERE Task_Name = N'PO_AI_CHAT'");
            }
            await SqlServerSchema.ApplyAsync(_fx.ConnectionString, new[] { SqlServerSchema.ApiMasterScript }, null, "http://other:1/");
            var all = (await Service().GetAllApi()).ApiData!;
            Assert.Equal("https://api.example/api/v1/HelpParams", all.Single(r => r.Task_Name == "PO_HELP_PARAMS").Api_Url);
            Assert.DoesNotContain(all, r => r.Task_Name == "PO_AI_CHAT");
            Assert.All(all.Where(r => r.Task_Name != "PO_HELP_PARAMS"), r => Assert.StartsWith("http://localhost:5080/api/v1/", r.Api_Url));
            await using (var c = new SqlConnection(_fx.ConnectionString))
                await c.ExecuteAsync("UPDATE dbo.API_MASTER SET Active_Status = 1, Api_Url = REPLACE(Api_Url, N'https://api.example/', N'http://localhost:5080/')");
        }
    }
}

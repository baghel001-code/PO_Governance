using Dapper;
using Infrastructure.Services.V1;
using Infrastructure.Utilitys;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace POGovernance.Tests.SqlServer
{
    /// <summary>
    /// 04_api_master.sql against a real server: API_MASTER + API_BASE_URL seed, PRO_GET_ALL_API (ALL / BY_TASK) through
    /// APIService, the base URL of each environment.
    /// </summary>
    public class ApiMasterSqlServerTests : IClassFixture<SqlServerFixture>
    {
        private readonly SqlServerFixture _fx;
        public ApiMasterSqlServerTests(SqlServerFixture fx) => _fx = fx;

        private APIService Service(string environment = "Development") =>
            new(new DapperHelper(_fx.ConnectionString), new ConfigurationBuilder().Build(), environment: environment);

        private async Task Sql(string sql)
        {
            await using var c = new SqlConnection(_fx.ConnectionString);
            await c.ExecuteAsync(sql);
        }

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
        public async Task Each_environment_gets_its_own_base_url()
        {
            var staging = (await Service("Staging").GetAllApi()).ApiData!;
            Assert.Equal("http://192.168.0.175:8178/api/v1/AuthLogin", staging.Single(r => r.Task_Name == "PO_AUTH_LOGIN").Api_Url);
            Assert.All(staging, r => Assert.StartsWith("http://192.168.0.175:8178/api/v1/", r.Api_Url));

            // Production is seeded inactive: GetAllApi says so instead of returning wrong URLs
            var prod = await Service("Production").GetAllApi();
            Assert.Equal(50424, prod.responseCode);
            Assert.Contains("environment Production", prod.responseMessage);

            await Sql("UPDATE dbo.API_BASE_URL SET Base_Url = N'https://pogov.example', Active_Status = 1 WHERE Base_Url_Key = N'POGOV_API' AND Environment = N'Production'");
            try
            {
                var p = (await Service("Production").GetAllApi()).ApiData!;
                Assert.Equal("https://pogov.example/api/v1/AuthLogin", p.Single(r => r.Task_Name == "PO_AUTH_LOGIN").Api_Url);   // final / added
            }
            finally
            {
                await Sql("UPDATE dbo.API_BASE_URL SET Base_Url = N'https://pogov-api.change-me.local/', Active_Status = 0 WHERE Base_Url_Key = N'POGOV_API' AND Environment = N'Production'");
            }
        }

        [SqlServerFact]
        public async Task Tokens_resolve_per_task_then_per_environment_then_from_API_MASTER()
        {
            var task = Infrastructure.Middleware.StaticTokenMiddleware.EncodeToken("tool1-staging");
            var env = Infrastructure.Middleware.StaticTokenMiddleware.EncodeToken("all-staging");
            await Sql($"INSERT INTO dbo.API_AUTHORIZATION (Task_Name, Environment, Api_Authorization) VALUES (N'PO_TOOL1_VALIDATE', N'Staging', N'{task}'), (N'*', N'Staging', N'{env}')");
            try
            {
                var staging = (await Service("Staging").GetAllApi()).ApiData!;
                var t1 = staging.Single(r => r.Task_Name == "PO_TOOL1_VALIDATE");
                Assert.Equal((task, "TASK", "Bearer"), (t1.Api_Authorization, t1.Auth_Source, t1.Api_Authorization_Type));
                var login = staging.Single(r => r.Task_Name == "PO_AUTH_LOGIN");
                Assert.Equal((env, "ENVIRONMENT"), (login.Api_Authorization, login.Auth_Source));
                var master = staging.Single(r => r.Task_Name == "PO_GET_ALL_API");   // start-up call: never overridden
                Assert.Equal("MASTER", master.Auth_Source);
                Assert.NotEqual(env, master.Api_Authorization);

                var dev = (await Service().GetAllApi()).ApiData!;                     // another environment: untouched
                Assert.All(dev, r => Assert.Equal("MASTER", r.Auth_Source));
                Assert.Equal(master.Api_Authorization, dev.Single(r => r.Task_Name == "PO_TOOL1_VALIDATE").Api_Authorization);
            }
            finally
            {
                await Sql("DELETE FROM dbo.API_AUTHORIZATION");
            }
        }

        [SqlServerFact]
        public async Task Rerunning_the_script_keeps_changed_urls_and_inactive_rows_are_hidden()
        {
            await Sql("UPDATE dbo.API_MASTER SET Base_Url_Key = NULL, Api_Url = N'https://help.example/api/v1/HelpParams' WHERE Task_Name = N'PO_HELP_PARAMS'");
            await Sql("UPDATE dbo.API_MASTER SET Active_Status = 0 WHERE Task_Name = N'PO_AI_CHAT'");
            await Sql("UPDATE dbo.API_BASE_URL SET Base_Url = N'http://dev-box:5080/' WHERE Base_Url_Key = N'POGOV_API' AND Environment = N'Development'");
            try
            {
                await SqlServerSchema.ApplyAsync(_fx.ConnectionString, new[] { SqlServerSchema.ApiMasterScript }, null, "http://other:1/");
                var all = (await Service().GetAllApi()).ApiData!;
                Assert.Equal("https://help.example/api/v1/HelpParams", all.Single(r => r.Task_Name == "PO_HELP_PARAMS").Api_Url);   // full URL, as is
                Assert.DoesNotContain(all, r => r.Task_Name == "PO_AI_CHAT");
                Assert.All(all.Where(r => r.Task_Name != "PO_HELP_PARAMS"), r => Assert.StartsWith("http://dev-box:5080/api/v1/", r.Api_Url));
            }
            finally
            {
                await Sql("UPDATE dbo.API_MASTER SET Active_Status = 1");
                await Sql("UPDATE dbo.API_MASTER SET Base_Url_Key = N'POGOV_API', Api_Url = N'api/v1/HelpParams' WHERE Task_Name = N'PO_HELP_PARAMS'");
                await Sql("UPDATE dbo.API_BASE_URL SET Base_Url = N'http://localhost:5080/' WHERE Base_Url_Key = N'POGOV_API' AND Environment = N'Development'");
            }
        }

        [SqlServerFact]
        public async Task Full_urls_from_before_API_BASE_URL_move_to_the_base_url_table()
        {
            await Sql("UPDATE dbo.API_MASTER SET Base_Url_Key = NULL, Api_Url = N'http://192.168.0.175:8178/' + Api_Url WHERE Api_Scope = N'PORTAL'");
            await Sql("DELETE FROM dbo.API_BASE_URL WHERE Base_Url_Key = N'POGOV_API' AND Environment = N'QA'");
            try
            {
                await SqlServerSchema.ApplyAsync(_fx.ConnectionString, new[] { SqlServerSchema.ApiMasterScript }, null, null, default, "QA");
                await using var c = new SqlConnection(_fx.ConnectionString);
                Assert.Equal(0, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.API_MASTER WHERE Base_Url_Key IS NULL"));
                Assert.Equal(2, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.API_MASTER WHERE Base_Url_Key = N'HRMS_API'"));
                Assert.Equal("http://192.168.0.175:8178/", await c.ExecuteScalarAsync<string>(
                    "SELECT Base_Url FROM dbo.API_BASE_URL WHERE Base_Url_Key = N'POGOV_API' AND Environment = N'QA'"));
                var qa = (await Service("QA").GetAllApi()).ApiData!;
                Assert.Equal("http://192.168.0.175:8178/api/v1/Health", qa.Single(r => r.Task_Name == "PO_HEALTH").Api_Url);
                Assert.Equal("http://localhost:5080/api/v1/Health", (await Service().GetAllApi()).ApiData!.Single(r => r.Task_Name == "PO_HEALTH").Api_Url);
            }
            finally
            {
                await Sql("DELETE FROM dbo.API_BASE_URL WHERE Environment = N'QA'");
            }
        }
    
        [SqlServerFact]
        public async Task HRMS_rows_are_server_only_and_never_get_the_portal_token()
        {
            var env = Infrastructure.Middleware.StaticTokenMiddleware.EncodeToken("all-staging");
            await Sql($"INSERT INTO dbo.API_AUTHORIZATION (Task_Name, Environment, Api_Authorization) VALUES (N'*', N'Staging', N'{env}')");
            try
            {
                // not in the portal's list, not reachable through GetSpecificApi
                var all = await Service("Staging").GetAllApi();
                Assert.Equal(0, all.responseCode);   // the inactive HRMS_API base URL does not break the portal's list
                Assert.DoesNotContain(all.ApiData!, r => r.Task_Name!.StartsWith("HRP_"));
                Assert.Equal(404, (await Service("Staging").GetSpecificApiData("HRP_GET_EMP_LOGIN_API", null)).responseCode);

                // POGovApi's own lookup: needs an active HRMS_API base URL
                var inactive = await Service("Staging").GetServerApiData("HRP_GET_EMP_LOGIN_API");
                Assert.Equal(50424, inactive.responseCode);
                Assert.Contains("HRMS_API", inactive.responseMessage);

                await Sql("UPDATE dbo.API_BASE_URL SET Base_Url = N'https://hrms.example/', Active_Status = 1 WHERE Base_Url_Key = N'HRMS_API' AND Environment = N'Staging'");
                await Sql("UPDATE dbo.API_MASTER SET Api_Url = N'api/EmpLogin' WHERE Task_Name = N'HRP_GET_EMP_LOGIN_API'");
                var row = Assert.Single((await Service("Staging").GetServerApiData("HRP_GET_EMP_LOGIN_API")).ApiData!);
                Assert.Equal("https://hrms.example/api/EmpLogin", row.Api_Url);
                Assert.Null(row.Api_Authorization);   // the '*' token of the portal APIs is never sent to HRMS
                Assert.Equal("MASTER", row.Auth_Source);
                Assert.Equal(404, (await Service("Staging").GetServerApiData("PO_AUTH_LOGIN")).responseCode);   // portal rows are not server rows

                // a token of its own is used
                var hrmsToken = Infrastructure.Middleware.StaticTokenMiddleware.EncodeToken("hrms-key");
                await Sql($"INSERT INTO dbo.API_AUTHORIZATION (Task_Name, Environment, Api_Authorization) VALUES (N'HRP_GET_EMP_LOGIN_API', N'Staging', N'{hrmsToken}')");
                row = Assert.Single((await Service("Staging").GetServerApiData("HRP_GET_EMP_LOGIN_API")).ApiData!);
                Assert.Equal((hrmsToken, "TASK"), (row.Api_Authorization, row.Auth_Source));
            }
            finally
            {
                await Sql("DELETE FROM dbo.API_AUTHORIZATION");
                await Sql("UPDATE dbo.API_BASE_URL SET Base_Url = N'https://hrms.change-me.local/', Active_Status = 0 WHERE Base_Url_Key = N'HRMS_API'");
                await Sql("UPDATE dbo.API_MASTER SET Api_Url = N'change-me/employee-login' WHERE Task_Name = N'HRP_GET_EMP_LOGIN_API'");
            }
        }
    }
}

using System.Reflection;
using System.Text.Json;
using Domain.Entities.Common;
using Infrastructure.Services.V1;
using Infrastructure.Utilitys;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace POGovernance.Tests.Architecture
{
    /// <summary>
    /// QPS conventions: every POGovApi action is <c>api/v1/[Action]</c>, has exactly one API_MASTER seed row
    /// (04_api_master.sql) with its URL and method, one AllApiNames / ApiNames property and one ApiNamesSettings
    /// entry in Admin/appsettings.json.
    /// </summary>
    public class ApiMasterConventionTests
    {
        private static readonly string[] GetActions = { "GetAllApi", "Health" };

        private static string RepoFile(params string[] parts)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "POGovernance.sln"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
        }

        private static List<(Type Controller, MethodInfo Action, string Method)> Actions() =>
            typeof(POGovApi.Controllers.V1.ApiControllerBase).Assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Select(m => (t, m, m.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).ToList()))
                    .Where(x => x.Item3.Count > 0)
                    .Select(x => (x.t, x.m, x.Item3.Single())))
                .ToList();

        private static List<ApiData> Seed() =>
            APIService.ParseSeed(File.ReadAllText(RepoFile("database", "sqlserver", "04_api_master.sql")), "http://x/", "tok");

        [Fact]
        public void Every_controller_uses_the_api_v1_Action_route_and_POST_except_master_and_health()
        {
            var actions = Actions();
            Assert.True(actions.Count >= 79, $"only {actions.Count} actions found");
            foreach (var (c, a, method) in actions)
            {
                var route = c.GetCustomAttribute<RouteAttribute>();
                Assert.True(route?.Template == "api/v1/[Action]", $"{c.Name}: route {route?.Template}");
                Assert.True(a.GetCustomAttributes<HttpMethodAttribute>().All(h => h.Template == null), $"{c.Name}.{a.Name} has a route template");
                Assert.Equal(GetActions.Contains(a.Name) ? "GET" : "POST", method);
            }
            Assert.Equal(actions.Count, actions.Select(x => x.Action.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void Every_action_has_one_API_MASTER_seed_row_with_its_url_and_method()
        {
            var seed = Seed();
            var actions = Actions().ToDictionary(x => x.Action.Name, x => x.Method);
            Assert.Equal(seed.Count, seed.Select(r => r.Task_Name).Distinct().Count());
            var seededActions = seed.Select(r => r.Api_Url!["http://x/api/v1/".Length..]).ToList();
            Assert.Equal(actions.Keys.OrderBy(x => x), seededActions.OrderBy(x => x));
            foreach (var r in seed)
            {
                var action = r.Api_Url!["http://x/api/v1/".Length..];
                Assert.Equal(actions[action], r.Api_Http_Method);
                Assert.True(r.Api_Timeout > 0);
                Assert.StartsWith("PO_", r.Task_Name);
            }
        }

        [Fact]
        public void AllApiNames_ApiNames_and_ApiNamesSettings_list_every_task_name()
        {
            var seedNames = Seed().Select(r => r.Task_Name!).OrderBy(x => x).ToList();
            Assert.Equal(seedNames, AllApiNames.All().OrderBy(x => x));
            var settingsProps = typeof(ApiNames).GetProperties().Select(p => p.Name).Where(n => n != "MasterApi").OrderBy(x => x);
            Assert.Equal(seedNames, settingsProps);

            using var doc = JsonDocument.Parse(File.ReadAllText(RepoFile("Admin", "appsettings.json")));
            var names = doc.RootElement.GetProperty("ApiNamesSettings").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
            Assert.Equal("All", names["MasterApi"]);
            names.Remove("MasterApi");
            Assert.Equal(seedNames, names.Keys.OrderBy(x => x));
            Assert.All(names, kv => Assert.Equal(kv.Key, kv.Value));
        }

        [Fact]
        public void The_static_token_is_the_same_encoded_text_in_both_appsettings_and_the_seed()
        {
            using var api = JsonDocument.Parse(File.ReadAllText(RepoFile("POGovApi", "appsettings.json")));
            using var portal = JsonDocument.Parse(File.ReadAllText(RepoFile("Admin", "appsettings.json")));
            var secret = api.RootElement.GetProperty("JwtSettings").GetProperty("SecretKey").GetString();
            Assert.Equal(secret, portal.RootElement.GetProperty("MasterApiSettings").GetProperty("Api_Authorization").GetString());
            Assert.Contains($"@ApiToken NVARCHAR(1000) = N'{secret}'", File.ReadAllText(RepoFile("database", "sqlserver", "04_api_master.sql")));
            Assert.Equal("PoGov-Static-Dev-Token-2026-Change-Me", Infrastructure.Middleware.StaticTokenMiddleware.DecodeToken(secret!));
        }

        [Fact]
        public void The_embedded_04_script_gets_the_configured_api_base()
        {
            var sql = SqlServerSchema.ApiMasterSql("https://pogov.example/api-host");
            Assert.Contains("DECLARE @ApiBase  NVARCHAR(400)  = N'https://pogov.example/api-host/';", sql);
            Assert.Contains(SqlServerSchema.ApiMasterScript, SqlServerSchema.DatabaseScripts);
            Assert.Equal(Seed().Count, APIService.ParseSeed(SqlServerSchema.ReadScript("04_api_master.sql"), "http://x/", null).Count);
        }

        [Fact]
        public void AllApiNames_Initialize_takes_the_ApiNamesSettings_values()
        {
            var before = AllApiNames.PO_HELP_PARAMS;
            try
            {
                AllApiNames.Initialize(new ApiNames { PO_HELP_PARAMS = "PO_HELP_PARAMS_V2", PO_TOOL1_TODAY = " " });
                Assert.Equal("PO_HELP_PARAMS_V2", AllApiNames.PO_HELP_PARAMS);
                Assert.Equal("PO_TOOL1_TODAY", AllApiNames.PO_TOOL1_TODAY);   // blank = keep the default
            }
            finally { AllApiNames.PO_HELP_PARAMS = before; }
        }
    }
}

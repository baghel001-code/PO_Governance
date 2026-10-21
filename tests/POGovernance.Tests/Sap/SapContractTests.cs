using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Application.Services.V1.Sap;
using DecisionEngine.Sap;
using DecisionEngine.Tool1;
using Domain.Entities.AppSettings;
using Domain.Entities.Sap;
using Microsoft.Extensions.Logging.Abstractions;
using POGovernance.Tests.Tool1;
using YamlDotNet.RepresentationModel;

namespace POGovernance.Tests.Sap
{
    /// <summary>
    /// The SAP contract classes and answers against the binding OpenAPI file
    /// (legacy/python-app/docs/po-governance-sap-api.yaml, spec v1.4): property names, required fields and enums.
    /// </summary>
    public class SapContractTests
    {
        private static readonly Lazy<YamlMappingNode> Root = new(() =>
        {
            var y = new YamlStream();
            y.Load(new StringReader(File.ReadAllText(SapVerdictMapTests.RepoPath("legacy", "python-app", "docs", "po-governance-sap-api.yaml"))));
            return (YamlMappingNode)y.Documents[0].RootNode;
        });

        private static YamlNode Get(YamlNode n, string key) => ((YamlMappingNode)n).Children[new YamlScalarNode(key)];
        private static bool Has(YamlNode n, string key) => n is YamlMappingNode m && m.Children.ContainsKey(new YamlScalarNode(key));

        /// <summary>Follows "$ref: '#/components/schemas/X'".</summary>
        private static YamlNode Resolve(YamlNode n)
        {
            while (Has(n, "$ref"))
            {
                var path = ((YamlScalarNode)Get(n, "$ref")).Value!.TrimStart('#', '/').Split('/');
                n = path.Aggregate((YamlNode)Root.Value, Get);
            }
            return n;
        }

        /// <summary>A schema by path: "Error", "DecisionResponse.items[]", "DecisionResponse.items[].metrics" or "paths:/reason-codes".</summary>
        private static YamlNode Schema(string path)
        {
            YamlNode n;
            var parts = path.Split('.');
            if (parts[0].StartsWith("paths:"))
                n = Get(Get(Get(Get(Get(Get(Get(Get(Root.Value, "paths"), parts[0]["paths:".Length..]), "get"), "responses"), "200"), "content"), "application/json"), "schema");
            else n = Get(Get(Get(Root.Value, "components"), "schemas"), parts[0]);
            n = Resolve(n);
            foreach (var p in parts.Skip(1))
            {
                var name = p.TrimEnd(']', '[');
                n = Resolve(Get(Get(n, "properties"), name));
                if (p.EndsWith("[]")) n = Resolve(Get(n, "items"));
            }
            return n;
        }

        private static List<string> Props(YamlNode schema) => ((YamlMappingNode)Get(schema, "properties")).Children.Keys.Select(k => ((YamlScalarNode)k).Value!).ToList();
        private static List<string> Required(YamlNode schema) => Has(schema, "required") ? ((YamlSequenceNode)Get(schema, "required")).Children.Select(c => ((YamlScalarNode)c).Value!).ToList() : new();
        private static List<string> Enum(string path, string prop)
        {
            var p = Resolve(Get(Get(Schema(path), "properties"), prop));
            if (Has(p, "items")) p = Resolve(Get(p, "items"));
            return ((YamlSequenceNode)Get(p, "enum")).Children.Select(c => ((YamlScalarNode)c).Value!).ToList();
        }

        private static List<string> JsonNames(Type t) =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.Always)
             .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? throw new Exception(t.Name + "." + p.Name + " has no [JsonPropertyName]")).ToList();

        public static IEnumerable<object[]> Pairs() => new[]
        {
            new object[] { typeof(SapError), "Error" },
            new object[] { typeof(SapPingResponse), "PingResponse" },
            new object[] { typeof(SapReasonCode), "ReasonCode" },
            new object[] { typeof(SapReasonCodesResponse), "paths:/reason-codes" },
            new object[] { typeof(SapDecisionRequest), "DecisionRequest" },
            new object[] { typeof(SapDecisionPurchaseOrder), "DecisionRequest.purchase_order" },
            new object[] { typeof(SapDecisionRequestItem), "DecisionRequest.purchase_order.items[]" },
            new object[] { typeof(SapDecisionResponse), "DecisionResponse" },
            new object[] { typeof(SapDataAsOf), "DecisionResponse.data_as_of" },
            new object[] { typeof(SapDecisionResponseItem), "DecisionResponse.items[]" },
            new object[] { typeof(SapDecisionMetrics), "DecisionResponse.items[].metrics" },
            new object[] { typeof(SapEventBatch), "EventBatch" },
            new object[] { typeof(SapEvent), "Event" },
            new object[] { typeof(SapPurchaseOrderState), "PurchaseOrderState" },
            new object[] { typeof(SapPurchaseOrderItem), "PurchaseOrderItem" },
            new object[] { typeof(SapGoodsMovement), "GoodsMovement" },
            new object[] { typeof(SapGovernanceOutcome), "GovernanceOutcome" },
            new object[] { typeof(SapEventBatchResult), "EventBatchResult" },
            new object[] { typeof(SapParkedEvent), "EventBatchResult.parked[]" },
        };

        [Theory]
        [MemberData(nameof(Pairs))]
        public void Contract_classes_have_exactly_the_YAML_property_names(Type type, string schema) =>
            Assert.Equal(Props(Schema(schema)).OrderBy(x => x), JsonNames(type).OrderBy(x => x));

        [Fact]
        public void The_contract_file_is_spec_v1_4()
        {
            Assert.Equal("1.4.0", ((YamlScalarNode)Get(Get(Root.Value, "info"), "version")).Value);
            var paths = ((YamlMappingNode)Get(Root.Value, "paths")).Children.Keys.Select(k => ((YamlScalarNode)k).Value).OrderBy(x => x);
            Assert.Equal(new[] { "/decisions", "/events", "/ping", "/reason-codes" }, paths);
            Assert.EndsWith("/api/sap/v1", ((YamlScalarNode)Get(((YamlSequenceNode)Get(Root.Value, "servers")).Children[0], "url")).Value);
        }

        [Fact]
        public void Enums_match_the_YAML()
        {
            Assert.Equal(Enum("DecisionRequest", "mode"), SapEnums.Modes);
            Assert.Equal(Enum("DecisionRequest", "otb_check"), SapEnums.OtbCheck);
            Assert.Equal(Enum("DecisionRequest", "depth_check"), SapEnums.DepthCheck);
            Assert.Equal(Enum("DecisionResponse", "mode"), SapEnums.Modes);
            Assert.Equal(Enum("DecisionResponse", "overall_verdict"), SapVerdicts.SpecValues);
            Assert.Equal(Enum("DecisionResponse.items[]", "verdict"), SapVerdicts.SpecValues);
            Assert.Equal(Enum("DecisionResponse", "verdict_code"), SapVerdicts.SpecValues.Select(SapVerdicts.Code));
            Assert.Equal(Enum("DecisionResponse.items[]", "verdict_code"), SapVerdicts.SpecValues.Select(SapVerdicts.Code));
            Assert.Equal(Enum("DecisionResponse", "route_to"), new[] { SapRoutes.None, SapRoutes.Buyer, SapRoutes.CatHead, SapRoutes.BmHead });
            Assert.Equal(Enum("Event", "event_type"), SapEnums.EventTypes);
            Assert.Equal(Enum("PurchaseOrderState", "release_status"), SapEnums.ReleaseStatus);
            Assert.Equal(Enum("GoodsMovement", "debit_credit"), SapEnums.DebitCredit);
            Assert.Equal(Enum("GovernanceOutcome", "outcome"), SapEnums.Outcomes);
            Assert.Equal(Enum("GovernanceOutcome", "by_role"), SapEnums.ByRoles);
            Assert.Equal(Enum("PingResponse", "environment"), SapEnums.Environments);
            Assert.Equal(new[] { "ok" }, Enum("PingResponse", "status"));
            // the default verdict mapping only produces values of the spec (BLOCK only by SapInterface:BlockVerdict = BLOCK)
            Assert.All(SapVerdictMap.Table, r => Assert.Contains(SapVerdictMap.VerdictFor(r, new SapInterfaceSettings().BlockVerdict), SapVerdicts.SpecValues));
        }

        /// <summary>Every key of an answer is a YAML property, every required property is present, every enum value allowed.</summary>
        private static void AssertConforms(JsonNode? node, YamlNode schema, string where)
        {
            schema = Resolve(schema);
            if (node is JsonObject o)
            {
                var props = Props(schema);
                foreach (var kv in o)
                {
                    Assert.True(props.Contains(kv.Key), where + "." + kv.Key + " is not in the YAML");
                    var ps = Resolve(Get(Get(schema, "properties"), kv.Key));
                    if (kv.Value is JsonValue v && Has(ps, "enum"))
                        Assert.Contains(v.ToString(), ((YamlSequenceNode)Get(ps, "enum")).Children.Select(c => ((YamlScalarNode)c).Value));
                    if (kv.Value != null) AssertConforms(kv.Value, ps, where + "." + kv.Key);
                    else Assert.True(Has(ps, "nullable"), where + "." + kv.Key + " is null but not nullable");
                }
                foreach (var r in Required(schema)) Assert.True(o.ContainsKey(r) && o[r] != null, where + ": required " + r + " missing");
            }
            else if (node is JsonArray a)
                foreach (var x in a) AssertConforms(x, Get(schema, "items"), where + "[]");
            else if (node is JsonValue v && Has(schema, "type"))
            {
                var type = ((YamlScalarNode)Get(schema, "type")).Value;
                var kind = v.GetValueKind();
                Assert.True(type switch
                {
                    "string" => kind == JsonValueKind.String,
                    "integer" => kind == JsonValueKind.Number && v.ToJsonString().All(c => char.IsDigit(c) || c == '-'),
                    "number" => kind == JsonValueKind.Number,
                    "boolean" => kind is JsonValueKind.True or JsonValueKind.False,
                    _ => true
                }, where + " should be " + type + " but is " + kind);
                if (type == "string" && Has(schema, "maxLength"))
                    Assert.True(v.GetValue<string>().Length <= int.Parse(((YamlScalarNode)Get(schema, "maxLength")).Value!), where + " too long");
            }
        }

        [Fact]
        public async Task Real_answers_conform_to_the_YAML()
        {
            var repo = new InMemorySapRepository();
            var clock = new FakeAppClock { TodayValue = SapKit.Today };
            var svc = new SapDecisionService(repo, new FakeDatasetCache { Data = SapKit.Data }, SapKit.Store(), clock, new SapDecisionEngine(new Tool1DecisionEngine()),
                                             new FakeEventStatus(), SapKit.Options(), NullLogger<SapDecisionService>.Instance);
            var req = SapKit.Request("WI-CONTRACT", SapKit.Item(10, "1101000101", "WHT", 50000), SapKit.Item(20, "1101000101", "GRY", 10),
                                     SapKit.Item(30, "2203000201", "OWH", 10), SapKit.Item(40, "2203000201", "BLU", 10), SapKit.Item(50, "1101000101", "RED", 10));
            AssertConforms(req, Schema("DecisionRequest"), "request");
            var ok = await svc.DecideAsync(req.ToJsonString(), SapKit.Caller);
            Assert.Equal(200, ok.Status);
            AssertConforms(JsonNode.Parse(ok.Json), Schema("DecisionResponse"), "response");
            var verdicts = JsonNode.Parse(ok.Json)!["items"]!.AsArray().Select(i => i!["verdict"]!.GetValue<string>()).ToHashSet();
            Assert.Equal(new[] { "ALLOW", "HOLD", "REDUCE", "WATCH" }, verdicts.OrderBy(x => x));

            foreach (var bad in new[] { await svc.DecideAsync("{", SapKit.Caller), await svc.DecideAsync(SapKit.Request("WI-U", SapKit.Item(10, "999", "X", 1)).ToJsonString(), SapKit.Caller) })
                AssertConforms(JsonNode.Parse(bad.Json), Schema("Error"), "error");

            var util = new SapUtilityService(repo, clock, SapKit.Options());
            AssertConforms(JsonSerializer.SerializeToNode(await util.PingAsync(SapKit.Caller), SapJson.Options), Schema("PingResponse"), "ping");
            AssertConforms(JsonSerializer.SerializeToNode(await util.ReasonCodesAsync(), SapJson.Options), Schema("paths:/reason-codes"), "reason-codes");
        }

        /// <summary>The YAML's own examples: the DecisionRequest example passes validation; the event examples are accepted.</summary>
        [Fact]
        public async Task The_YAML_examples_are_valid_requests()
        {
            JsonNode Example(params string[] path) => Yaml2Json(path.Aggregate((YamlNode)Root.Value, Get));

            var decision = Example("paths", "/decisions", "post", "requestBody", "content", "application/json", "example");
            Assert.Null(SapRequestValidator.Validate(JsonSerializer.Deserialize<SapDecisionRequest>(decision.ToJsonString(), SapJson.Options)));
            AssertConforms(Example("paths", "/decisions", "post", "responses", "200", "content", "application/json", "example"), Schema("DecisionResponse"), "yaml example");

            var svc = new SapEventService(new InMemorySapRepository(), new FakeAppClock { TodayValue = SapKit.Today }, SapKit.Options(), NullLogger<SapEventService>.Instance);
            foreach (var name in new[] { "goodsReceipt", "poReleased" })
            {
                var batch = Example("paths", "/events", "post", "requestBody", "content", "application/json", "examples", name, "value");
                var r = JsonNode.Parse((await svc.ReceiveAsync(batch.ToJsonString(), SapKit.Caller)).Json)!;
                Assert.Single(r["accepted"]!.AsArray());
                Assert.Empty(r["parked"]!.AsArray());
                AssertConforms(r, Schema("EventBatchResult"), "events answer");
            }
        }

        /// <summary>YAML example → JSON (numbers, booleans and null by the scalar's form; quoted scalars stay strings).</summary>
        private static JsonNode Yaml2Json(YamlNode n)
        {
            switch (n)
            {
                case YamlMappingNode m:
                    var o = new JsonObject();
                    foreach (var kv in m.Children) o[((YamlScalarNode)kv.Key).Value!] = kv.Value is YamlScalarNode { Value: "null" } ? null : Yaml2Json(kv.Value);
                    return o;
                case YamlSequenceNode s:
                    return new JsonArray(s.Children.Select(c => (JsonNode?)Yaml2Json(c)).ToArray());
                default:
                    var sc = (YamlScalarNode)n;
                    var v = sc.Value ?? "";
                    if (sc.Style is YamlDotNet.Core.ScalarStyle.SingleQuoted or YamlDotNet.Core.ScalarStyle.DoubleQuoted) return JsonValue.Create(v)!;
                    if (v is "true" or "false") return JsonValue.Create(v == "true")!;
                    if (long.TryParse(v, out var l)) return JsonValue.Create(l)!;
                    if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return JsonValue.Create(d)!;
                    return JsonValue.Create(v)!;
            }
        }
    }
}

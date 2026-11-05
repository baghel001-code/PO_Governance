using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Services.V1.Tool1;
using DecisionEngine.Model;
using DecisionEngine.Mra;
using DecisionEngine.Recommendation;
using DecisionEngine.Results.Mra;
using DecisionEngine.Tool1;
using Domain.Constants;
using Domain.Records;
using POGovernance.Tests.Tool1;

namespace POGovernance.Tests.Mra
{
    /// <summary>v1.1.7: open MRAs lower the single concept Max Permissible; switch 1 OFF = check only, today's approvals not counted.</summary>
    public class MraEngineTests
    {
        private const string Today = "2026-10-03", Gm = "1101000101", Month = "12.2026";
        private static readonly AppUserContext Buyer = new("b@vmm.test", "Buyer One", new[] { Roles.Buyer }, false);

        private static EngineData Data(bool toolReq = true)
        {
            var d = Tool1Fixture.Data("S1-early-month");
            if (toolReq) return d;
            var p = d.Params.Clone();
            p.Extra ??= new Dictionary<string, JsonElement>();
            p.Extra[MraSwitches.ToolReqKey] = JsonSerializer.SerializeToElement(false);
            return d.WithParams(p);
        }

        private static MraCapacity Cap(EngineData data, string colour, long qty, string level = "1") =>
            MraCapacity.Build(data, MraReplay.Run(new Dictionary<string, MraFileRecord>
            {
                ["900201"] = new() { Ga = Gm, Colour = colour, Qty = qty, Dd = "2026-12-15", Level = level }
            }, null), Today, livePo: false);

        private static string AllowColour(EngineData data, ITool1DecisionEngine eng)
        {
            foreach (var c in new[] { "BLK", "WHT", "NAVY", "RED", "GRY", "BLU" })
            {
                var info = Tool1Router.Resolve(data, Gm);
                var r = eng.RunEngineB(data, Array.Empty<JsonObject>(), Today, Gm, c, Month, 100, info);
                if (r.MaxPermQty is > 2000) return c;
            }
            throw new InvalidOperationException("no colour with headroom in the fixture");
        }

        [Fact]
        public void Open_mras_lower_max_permissible_by_their_qty_and_show_on_the_card()
        {
            var data = Data();
            var eng = new Tool1DecisionEngine(new RecommendationEngine());
            var colour = AllowColour(data, eng);
            var info = Tool1Router.Resolve(data, Gm);
            var before = eng.RunEngineB(data, Array.Empty<JsonObject>(), Today, Gm, colour, Month, 100, info);
            Tool1DecisionEngineBResult(before);
            using (MraScope.Use(Cap(data, colour, 1000)))
            {
                var after = eng.RunEngineB(data, Array.Empty<JsonObject>(), Today, Gm, colour, Month, 100, info);
                Assert.Equal(before.MaxPermQty!.Value - 1000, after.MaxPermQty);
                Assert.Equal(1000, after.MraIn!.ByMon[Month]);
                Assert.Equal("900201", after.MraIn.Items.Single().No);
                var o = eng.Decide(data, Array.Empty<JsonObject>(), Today, 1, Gm, colour, Month, 100, null);
                Assert.Equal(1000, eng.BuildCard(o, data.Params, Today).MraIn!.ByMon[Month]);
            }
            // level 3 / 7 / X do not count
            using (MraScope.Use(Cap(data, colour, 1000, "7")))
                Assert.Equal(before.MaxPermQty, eng.RunEngineB(data, Array.Empty<JsonObject>(), Today, Gm, colour, Month, 100, info).MaxPermQty);
        }

        private sealed class FixedSource : Application.Interfaces.V1.Sap.IMraCapacitySource
        {
            private readonly MraCapacity _cap;
            public FixedSource(MraCapacity cap) => _cap = cap;
            public async Task<MraCapacity?> GetAsync(EngineData data, CancellationToken ct = default) { await Task.Yield(); return _cap; }
            public Task<(Application.Interfaces.V1.Sap.MraStoredState Stored, MraCapacity Capacity)?> GetFullAsync(EngineData data, CancellationToken ct = default) =>
                Task.FromResult<(Application.Interfaces.V1.Sap.MraStoredState, MraCapacity)?>(null);
            public void Invalidate() { }
        }

        [Fact]
        public async Task The_service_counts_the_mras_of_its_source()
        {
            var data = Data();
            var eng = new Tool1DecisionEngine(new RecommendationEngine());
            var colour = AllowColour(data, eng);
            var svc = new Tool1Service(new FakeDatasetCache { Data = data }, new FakeRequestLedger { Today = Today }, new FakeAppClock { TodayValue = Today }, eng,
                                       null, new FixedSource(Cap(data, colour, 1000)));
            var v = await svc.ValidateAsync(new DecisionEngine.Results.Tool1.Tool1SubmitInputDto { Gm = Gm, Colour = colour, Month = Month, Qty = 100 }, Buyer);
            Assert.Equal(1000, v.Card!.MraIn!.ByMon[Month]);
            Assert.Null(MraScope.Current);   // the scope ends with the call
        }

        private static void Tool1DecisionEngineBResult(DecisionEngine.Results.Tool1.Tool1EngineBResult r) => Assert.Null(r.MraIn);

        [Fact]
        public async Task Switch_1_off_makes_the_request_check_only_and_drops_todays_approvals()
        {
            var on = Data();
            var eng = new Tool1DecisionEngine(new RecommendationEngine());
            var colour = AllowColour(on, eng);
            var info = Tool1Router.Resolve(on, Gm);
            var approved = new JsonObject
            {
                ["reqDate"] = Today, ["gm"] = Gm, ["colour"] = colour, ["month"] = Month, ["qty"] = 500, ["engine"] = "B", ["decision"] = "Auto Allow",
                ["finalApprovedQty"] = 500, ["info"] = JsonNode.Parse(JsonSerializer.Serialize(info))
            };
            var withLedger = eng.RunEngineB(on, new[] { approved }, Today, Gm, colour, Month, 100, info);
            var noLedger = eng.RunEngineB(on, Array.Empty<JsonObject>(), Today, Gm, colour, Month, 100, info);
            Assert.True(withLedger.MaxPermQty < noLedger.MaxPermQty);
            var off = Data(toolReq: false);
            Assert.Equal(noLedger.MaxPermQty, eng.RunEngineB(off, new[] { approved }, Today, Gm, colour, Month, 100, info).MaxPermQty);

            var ledger = new FakeRequestLedger { Today = Today };
            var svc = new Tool1Service(new FakeDatasetCache { Data = off }, ledger, new FakeAppClock { TodayValue = Today }, eng);
            var input = new DecisionEngine.Results.Tool1.Tool1SubmitInputDto { Gm = Gm, Colour = colour, Month = Month, Qty = 100 };
            var v = await svc.ValidateAsync(input, Buyer);
            Assert.Equal(("checkOnly", true, false), (v.Status, v.CheckOnly, v.NeedsConfirmation));
            Assert.True(v.Card!.CheckOnly);
            var s = await svc.SubmitAsync(input, Buyer);
            Assert.Equal("checkOnly", s.Status);
            Assert.Empty(ledger.Posts);
        }
    }
}

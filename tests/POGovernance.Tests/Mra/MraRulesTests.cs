using System.Text.Json.Nodes;
using DecisionEngine.Model;
using DecisionEngine.Mra;

namespace POGovernance.Tests.Mra
{
    /// <summary>v1.1.7 MRA rules: the Python dev/tests/test_mra.py cases (normalisers, messages, replay) and app/mra.js classify / index.</summary>
    public class MraRulesTests
    {
        [Theory]
        [InlineData("1", "1")] [InlineData("01", "1")] [InlineData("1.0", "1")] [InlineData("First Level", "1")] [InlineData(" first level ", "1")]
        [InlineData("SECOND LEVEL", "2")] [InlineData("Third Level", "3")] [InlineData("Fourth Level", "4")] [InlineData("final", "5")] [InlineData("Final", "5")]
        [InlineData("Discarded", "6")] [InlineData("Self Reject", "7")] [InlineData("self-reject", "7")] [InlineData("Forced Approved", "8")]
        [InlineData("Draft", "9")] [InlineData("x", "X")] [InlineData("X", "X")] [InlineData("Reject", "X")] [InlineData("rejected", "X")]
        public void Levels(string v, string want) => Assert.Equal(want, MraNorm.Level(v));

        [Theory]
        [InlineData("")] [InlineData(null)] [InlineData("10")] [InlineData("0")] [InlineData("zz")] [InlineData("Level")] [InlineData("11")]
        public void Bad_levels(string? v) => Assert.Null(MraNorm.Level(v));

        [Theory]
        [InlineData("3", "3")] [InlineData("Released", "3")] [InlineData("released", "3")] [InlineData("Unreleased", "2")] [InlineData("1", "1")]
        [InlineData("2", "2")] [InlineData("Created", "1")] [InlineData("short closed", "C")] [InlineData("", null)] [InlineData("9", null)]
        public void Po_status(string v, string? want) => Assert.Equal(want, MraNorm.PoStatus(v));

        [Theory]
        [InlineData("2026-11-02", "2026-11-02")] [InlineData("2026-11-02T00:00:00", "2026-11-02")] [InlineData("02.11.2026", "2026-11-02")]
        [InlineData("02/11/2026", "2026-11-02")] [InlineData("20261102", "2026-11-02")] [InlineData("31.02.2026", "")] [InlineData("x", "")]
        public void Dates(string v, string want) => Assert.Equal(want, MraNorm.Date(v));

        [Fact]
        public void Articles_numbers_and_qty()
        {
            Assert.Equal("1121103017", MraNorm.Ga("000000001121103017"));
            Assert.Equal("1121103017", MraNorm.Ga("1121103017"));
            Assert.Equal(6000, MraNorm.Qty("6,000"));
            Assert.Null(MraNorm.Qty("abc"));
            Assert.True(MraNorm.ValidNo("900101") && MraNorm.ValidNo("MRA-1_a"));
            Assert.False(MraNorm.ValidNo("1');alert(1);('") || MraNorm.ValidNo(""));
        }

        private static Dictionary<string, MraFileRecord> Snap() => new()
        {
            ["100"] = new() { Ga = "1112045675", Colour = "LGY", Qty = 5000, Dd = "2026-12-31", Level = "1" },
            ["101"] = new() { Ga = "1112045675", Colour = "LGY", Qty = 3000, Dd = "2026-12-31", Level = "5", Pos = { new MraFilePo { Po = "5900000001", Status = "3" } } },
            ["102"] = new() { Ga = "1112045675", Colour = "LGY", Qty = 2000, Dd = "2026-12-31", Level = "8" },
        };

        private static MraFeedEvent Ev(int seq, string kind, string json)
        {
            var raw = JsonNode.Parse(json);
            var e = new MraFeedEvent { Seq = seq, Kind = kind, At = new DateTimeOffset(2026, 11, 5, 10, seq, 0, TimeSpan.FromHours(5.5)), Raw = json };
            if (kind == "mra") { var (m, err) = MraMessage.Parse(raw); e.Mra = m; e.Error = err; }
            else { var (m, err) = PoMessage.Parse(raw); e.Po = m; e.Error = err; }
            return e;
        }

        [Fact]
        public void Replay_like_the_python_tool()
        {
            var evs = new[]
            {
                Ev(1, "mra", """{"mra_no":"0000000200","level":"First Level","generic_article":"000000001112045675","colour":"lgy","qty":"6,000","delivery_date":"2026-12-31","msg_id":"a"}"""),
                Ev(2, "mra", """{"mra_no":"200","level":"1","generic_article":"1112045675","colour":"LGY","qty":6000,"delivery_date":"2026-12-31","msg_id":"a"}"""),
                Ev(3, "mra", """{"mra_no":"200","level":"Second Level"}"""),
                Ev(4, "mra", """{"mra_no":"300","level":"1"}"""),
                Ev(5, "mra", """{"mra_no":"200","level":"bogus"}"""),
                Ev(6, "mra", """{"mra_no":"100","level":"7"}"""),
                Ev(7, "mra", """{"mra_no":"102","level":"1"}"""),
                Ev(8, "mra", """{"mra_no":"200","level":"4","qty":5500}"""),
                Ev(9, "mra", """{"mra_no":"200","level":"final","po_no":"5900000099","po_status":"Released"}"""),
                Ev(10, "po", """{"po_no":"5900000099","mra_no":"200","status":"3","qty":5500}"""),
                Ev(11, "mra", """{"mra_no":"200","level":"2","occurred_at":"2026-11-05T09:00:00"}"""),
            };
            var st = MraReplay.Run(Snap(), evs);
            var res = st.Results.ToDictionary(r => r.Seq);
            var m = st.Mras;
            Assert.True(res[1].Outcome == "applied" && m["200"].CreatedLive);
            Assert.Equal("duplicate", res[2].Outcome);
            Assert.Equal("parked", res[4].Outcome);
            Assert.Equal("new MRA without ga, colour, qty, dd", res[4].Reason);
            Assert.Equal("parked", res[5].Outcome);
            Assert.Equal("7", m["100"].Level);
            Assert.True(m["102"].Level == "1" && m["102"].Was8);
            Assert.True(!m["101"].L5Live && m["101"].PoStatus == "3");
            Assert.True(!m["200"].L5Live && m["200"].Level == "2");
            Assert.Equal(5500, m["200"].Qty);
            Assert.True(st.Pos["5900000099"].Status == "3" && st.Pos["5900000099"].Mra == "200");
            Assert.Equal(new[] { "1", "2", "4", "5", "2" }, m["200"].History.Select(h => h.To));
        }

        [Fact]
        public void Older_message_is_ignored_and_live_level_5_is_flagged()
        {
            var st2 = MraReplay.Run(Snap(), new[]
            {
                Ev(1, "mra", """{"mra_no":"100","level":"2","occurred_at":"2026-11-05T12:00:00"}"""),
                Ev(2, "mra", """{"mra_no":"100","level":"7","occurred_at":"2026-11-05T11:00:00"}""")
            });
            Assert.Equal("2", st2.Mras["100"].Level);
            Assert.Equal("older", st2.Results[1].Outcome);
            Assert.True(MraReplay.Run(Snap(), new[] { Ev(1, "mra", """{"mra_no":"100","level":"5"}""") }).Mras["100"].L5Live);
        }

        [Fact]
        public void Times_with_zones_and_unreadable_times()
        {
            Assert.True(MraNorm.Older("2026-11-05T10:12:00+05:30", "2026-11-05T05:00:00Z"));      // 10:12 IST is before 10:30 IST
            Assert.False(MraNorm.Older("2026-11-05T05:00:00Z", "2026-11-05T10:12:00+05:30"));
            Assert.False(MraNorm.Older("yesterday", "2026-11-05T10:12:00"));
        }

        [Fact]
        public void Numbers_without_zeros_and_po_status_with_level_5()
        {
            var (o, e) = MraMessage.Parse(JsonNode.Parse("""{"mra_no":"1');x('","level":"1"}"""));
            Assert.Null(o); Assert.Contains("not valid", e);
            var (p, _) = PoMessage.Parse(JsonNode.Parse("""{"po_no":"0045001","status":"3","mra_no":"00900201"}"""));
            Assert.Equal(("45001", "900201"), (p!.Po, p.Mra));

            var snap = new Dictionary<string, MraFileRecord> { ["9"] = new() { Ga = "1", Colour = "A", Qty = 10, Dd = "2026-12-01", Level = "1", Pos = { new MraFilePo { Po = "0045001" } } } };
            var st4 = MraReplay.Run(snap, new[] { Ev(1, "po", """{"po_no":"45001","status":"2"}"""), Ev(2, "po", """{"po_no":"0045001","status":"3"}""") });
            Assert.Equal(new[] { "45001" }, st4.Pos.Keys);
            Assert.Equal("45001", st4.Mras["9"].PoNo);
            Assert.Equal(10, st4.Mras["9"].FileQty);

            var st5 = MraReplay.Run(Snap(), new[]
            {
                Ev(1, "mra", """{"mra_no":"100","level":"5","po_no":"59001","po_status":"Unreleased"}"""),
                Ev(2, "mra", """{"mra_no":"100","level":"5","po_no":"59001","po_status":"3"}""")
            });
            Assert.True(st5.Mras["100"].L5Live && st5.Mras["100"].MraPoStatus == "3" && st5.Results[1].Outcome == "applied");
            var st6 = MraReplay.Run(Snap(), new[] { Ev(1, "mra", """{"mra_no":"100","level":"5","po_status":"2"}"""), Ev(2, "po", """{"po_no":"1","mra_no":"100","status":"3"}""") });
            Assert.Equal("2", st6.Mras["100"].MraPoStatus);
        }

        [Fact]
        public void Decision_fields_are_kept_and_unsafe_urls_dropped()
        {
            var (o, _) = MraMessage.Parse(JsonNode.Parse("""{"mra_no":"5","level":"1","verdict":"Hold — Don't Raise","reason":"Max 0","decision_id":"D-9","max_permissible":"1,200","decision_url":"javascript:alert(1)","working":{"a":1}}"""));
            var d = o!.DecisionInfo!;
            Assert.StartsWith("Hold", d.Decision);
            Assert.Equal(("Max 0", "D-9", 1200L), (d.DecisionReason, d.DecisionId, d.DecisionMaxPerm!.Value));
            Assert.Null(d.DecisionUrl);
            Assert.Equal(1, (int)d.Working!["a"]!);
            var st7 = MraReplay.Run(Snap(), new[] { Ev(1, "mra", """{"mra_no":"100","level":"1","decision":"Auto Allow"}"""), Ev(2, "mra", """{"mra_no":"100","level":"2"}""") });
            Assert.Equal("Auto Allow", st7.Mras["100"].Decision.Decision);
            Assert.Equal("applied", st7.Results[0].Outcome);
        }

        // ── classification and capacity (app/mra.js) ──────────────────────
        private static SheetRow R(params (string K, object? V)[] cells) => new(cells.Select(c => new KeyValuePair<string, object?>(c.K, c.V)));

        private static EngineData Data(bool withF2 = false)
        {
            var am = new List<SheetRow>
            {
                R(("Generic Material", "1112045675"), ("Brand Type", "NON-PL"), ("MC CD", "121030002"), ("MRP", "799"), ("Flag", "NON-BOGO"), ("ART DESC", "MENS KURTA")),
                R(("Generic Material", "2222222222"), ("Brand Type", "NON-PL"), ("MC CD", "121030002"), ("MRP", "799"), ("Flag", "NON-BOGO"))
            };
            var um = new List<SheetRow> { R(("Generic Material", "1112045675")) };           // single concept; 2222222222 is not in UM → multi concept
            var scr = new List<SheetRow> { R(("Brand Type", "NON-PL"), ("MC CD", "121030002"), ("MRP", "799"), ("Level_2_Buyer_Email_ID", "DH@x.in"), ("Category_Email_ID", "ch@x.in")) };
            var files = new Dictionary<string, IReadOnlyList<SheetRow>?> { [DatasetKeys.ArtMaster] = am, [DatasetKeys.Um] = um, [DatasetKeys.CatHeadMap] = scr };
            if (withF2) files[DatasetKeys.F2] = new List<SheetRow>
            {
                R(("PO Number", "0059000001"), ("Generic Material", "1112045675"), ("Color", "LGY"), ("Delivery Month", "12.2026"), ("Pending Qty", 3000))
            };
            return new EngineData(files, new EngineParams(), 1, 1);
        }

        [Fact]
        public void Levels_1_2_4_count_until_the_delivery_date()
        {
            var snap = new Dictionary<string, MraFileRecord>
            {
                ["1"] = new() { Ga = "1112045675", Colour = "BLK", Qty = 5000, Dd = "2026-12-20", Level = "1" },
                ["2"] = new() { Ga = "1112045675", Colour = "BLK", Qty = 4000, Dd = "2026-12-05", Level = "2" },
                ["3"] = new() { Ga = "1112045675", Colour = "BLK", Qty = 1000, Dd = "2026-12-31", Level = "4" },
                ["4"] = new() { Ga = "1112045675", Colour = "BLK", Qty = 700, Dd = "2026-12-31", Level = "3" },
                ["5"] = new() { Ga = "1112045675", Colour = "BLK", Qty = 800, Dd = "2026-12-31", Level = "8" },
                ["6"] = new() { Ga = "1112045675", Colour = "BLK", Qty = 900, Dd = "2026-11-04", Level = "1" },     // delivery date passed
                ["7"] = new() { Ga = "1112045675", Colour = "BLK", Qty = 600, Dd = "2026-12-31", Level = "5" },     // final in the file
                ["8"] = new() { Ga = "2222222222", Colour = "BLK", Qty = 500, Dd = "2026-12-31", Level = "1" },     // multi concept
                ["9"] = new() { Ga = "3333333333", Colour = "BLK", Qty = 400, Dd = "2026-12-31", Level = "1" },     // not in Article Master
            };
            var cap = MraCapacity.Build(Data(), MraReplay.Run(snap, null), "2026-11-05", livePo: false);
            var inc = cap.IncomingFor("NON-PL", "121030002", "799", "NON-BOGO", "blk")!;
            Assert.Equal(10000, inc.ByMon["12.2026"]);   // the release-notes example: 5,000 + 4,000 + 1,000
            Assert.Equal(new[] { "1", "2", "3" }, inc.Items.Select(i => i.No).OrderBy(x => x));
            var c = cap.Rows.ToDictionary(r => r.No, r => r.C);
            Assert.True(c["6"].DdPassed);
            Assert.True(c["8"].SetAside && c["9"].SetAside);
            Assert.Equal("Not in Article Master", c["9"].Why);
            Assert.Equal(("Division Head", "dh@x.in"), (c["1"].Queue, c["1"].DhEmail));
            Assert.Equal("Category Head", c["2"].Queue);
            Assert.False(c["7"].Counted);
            Assert.Equal(new Dictionary<string, long> { ["12.2026"] = 10000 }, cap.ByColourFor("121030002", "799", "NON-PL", "NON-BOGO")["blk"]);
        }

        [Fact]
        public void Live_changes_move_capacity()
        {
            var snap = new Dictionary<string, MraFileRecord>
            {
                ["1"] = new() { Ga = "1112045675", Colour = "LGY", Qty = 5000, Dd = "2026-12-20", Level = "1" },
                ["2"] = new() { Ga = "1112045675", Colour = "LGY", Qty = 3000, Dd = "2026-12-20", Level = "8" },
                ["3"] = new() { Ga = "1112045675", Colour = "LGY", Qty = 2000, Dd = "2026-12-20", Level = "4" },
                ["4"] = new() { Ga = "1112045675", Colour = "LGY", Qty = 1000, Dd = "2026-12-20", Level = "4" },
            };
            var evs = new[]
            {
                Ev(1, "mra", """{"mra_no":"1","level":"7"}"""),                                     // self reject → 5,000 back
                Ev(2, "mra", """{"mra_no":"2","level":"1"}"""),                                     // 8 → 1 → 3,000 counted
                Ev(3, "mra", """{"mra_no":"3","level":"5","po_no":"59","po_status":"3"}"""),        // final today, released → still counted
                Ev(4, "mra", """{"mra_no":"4","level":"5","po_no":"60","po_status":"2"}"""),        // final today, unreleased → not counted
                Ev(5, "mra", """{"mra_no":"9","level":"2","generic_article":"1112045675","colour":"LGY","qty":700,"delivery_date":"2026-12-10"}"""),
            };
            var st = MraReplay.Run(snap, evs);
            var cap = MraCapacity.Build(Data(), st, "2026-11-05", livePo: false);
            Assert.Equal(3000 + 2000 + 700, cap.IncomingFor("NON-PL", "121030002", "799", "NON-BOGO", "LGY")!.ByMon["12.2026"]);
            var log = evs.Select(e => (e, (MraFeedResult?)st.Results.Single(r => r.Seq == e.Seq))).ToList();
            var eff = cap.Effects(log).Select(x => x.Text).ToArray();
            Assert.Equal(new[] { "▲ 5,000 up", "▼ 3,000 down", "no change (counted until tomorrow)", "▲ 1,000 up (PO unreleased)", "▼ 700 down" }, eff);
        }

        [Fact]
        public void Live_po_change_replaces_the_mra_and_the_f2_line_only_with_switch_2()
        {
            var snap = new Dictionary<string, MraFileRecord>
            {
                ["1"] = new() { Ga = "1112045675", Colour = "LGY", Qty = 5000, Dd = "2026-12-20", Level = "4", Pos = { new MraFilePo { Po = "59000002" } } },
            };
            var evs = new[]
            {
                Ev(1, "po", """{"po_no":"59000002","status":"3","qty":4500}"""),                  // the MRA's PO released with 4,500
                Ev(2, "po", """{"po_no":"59000001","status":"2"}"""),                            // F2 PO unreleased → its 3,000 comes back
            };
            var st = MraReplay.Run(snap, evs);
            var off = MraCapacity.Build(Data(withF2: true), st, "2026-11-05", livePo: false);
            Assert.Equal(5000, off.IncomingFor("NON-PL", "121030002", "799", "NON-BOGO", "LGY")!.ByMon["12.2026"]);
            var on = MraCapacity.Build(Data(withF2: true), st, "2026-11-05", livePo: true);
            Assert.Equal(4500 - 3000, on.IncomingFor("NON-PL", "121030002", "799", "NON-BOGO", "LGY")!.ByMon["12.2026"]);
            Assert.False(on.Rows.Single().C.Counted);
            Assert.StartsWith("PO 59000002 has a live change", on.Rows.Single().C.Why);
        }

        [Theory]
        [InlineData("Auto Allow", "Allow")] [InlineData("Hold — Don't Raise", "Hold")] [InlineData("No Plan – Review", "No Plan")]
        [InlineData("Recommend Reduced Qty", "Reduce")] [InlineData("", "Not sent")] [InlineData("Watch List", "Watch List")]
        public void Decision_groups(string d, string g) => Assert.Equal(g, MraCapacity.DecisionGroup(d));
    }
}

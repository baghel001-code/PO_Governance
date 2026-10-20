using System.Text.Json;
using DecisionEngine.Grc;
using DecisionEngine.Model;
using DecisionEngine.Results.Analytics.Grc;

namespace POGovernance.Tests.Analytics.Grc
{
    /// <summary>
    /// Parity of the GRC Back Testing port against the ORIGINAL index.html functions (expected.json is
    /// produced by tests/parity/grc/run.js on the synthetic fixture).
    /// </summary>
    public sealed class GrcParityTests : IClassFixture<GrcParityFixture>
    {
        private readonly GrcParityFixture _f;
        public GrcParityTests(GrcParityFixture f) => _f = f;

        // ── soft-assert helpers ────────────────────────────────────────────
        private sealed class Diff
        {
            public readonly List<string> Errors = new();
            public void Eq(string? exp, string? act, string where) { if (exp != act) Add($"{where}: expected «{exp}» got «{act}»"); }
            public void Num(double exp, double act, string where) { if (!(exp == act || (double.IsNaN(exp) && double.IsNaN(act)))) Add($"{where}: expected {exp:R} got {act:R}"); }
            public void True(bool c, string where) { if (!c) Add(where); }
            public void Seq(IEnumerable<string?> exp, IEnumerable<string?> act, string where)
            {
                var e = exp.ToList(); var a = act.ToList();
                if (!e.SequenceEqual(a)) Add($"{where}: expected [{string.Join(", ", e)}] got [{string.Join(", ", a)}]");
            }
            private void Add(string s) { if (Errors.Count < 60) Errors.Add(s); }
            public void Done() => Assert.True(Errors.Count == 0, string.Join("\n", Errors));
        }

        private static string? Str(JsonElement e, string p) =>
            e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        private static List<string> StrList(JsonElement e) =>
            e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => x.GetString()!).ToList() : new();

        private static GrcImpactRequest Request(JsonElement q) => new()
        {
            AsOfPeriod = Str(q, "asOfPeriod"), ViewMode = Str(q, "viewMode") ?? "native", Cutover = Str(q, "cutover"),
            Division = Str(q, "division"), CatHead = Str(q, "catHead"), Brand = Str(q, "brand"), DrillPeriod = Str(q, "drill"),
        };

        private static JsonElement? ParamsOf(JsonElement sc) =>
            sc.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object ? p : null;

        // ── GRC Impact screen (renderGRCImpact) ────────────────────────────
        [Fact]
        public void Impact_screen_matches_original()
        {
            var d = new Diff();
            var svc = GrcTestServices.Grc();
            foreach (var sc in _f.E.GetProperty("impact").EnumerateArray())
            {
                var name = sc.GetProperty("name").GetString()!;
                var data = _f.Data(sc.GetProperty("ds").GetString()!, ParamsOf(sc));
                var dto = svc.Compute(data, Request(sc.GetProperty("req")));
                var c = dto.Controls;

                d.Seq(StrList(sc.GetProperty("seasonCurrent")), dto.SeasonCurrent, name + " seasonCurrent");
                d.Seq(StrList(sc.GetProperty("seasonFallback")), dto.SeasonFallback, name + " seasonFallback");
                d.Eq(Str(sc, "asOf"), c.AsOfPeriod, name + " asOf");
                var sel = sc.GetProperty("selects");
                void Select(string key, string value, List<string> options, bool hasAll)
                {
                    var s = sel.GetProperty(key);
                    d.Eq(s.GetProperty("value").GetString(), value, $"{name} select {key} value");
                    d.Seq(StrList(s.GetProperty("options")), (hasAll ? new[] { "" } : Array.Empty<string>()).Concat(options), $"{name} select {key} options");
                }
                Select("asOf", c.AsOfPeriod ?? "", c.AsOfOptions, false);
                Select("cutover", c.Cutover ?? "", c.CutoverOptions, false);
                Select("division", c.Division, c.DivisionOptions, true);
                Select("catHead", c.CatHead, c.CatHeadOptions, true);
                Select("brand", c.Brand, c.BrandOptions, true);
                Select("drill", c.DrillPeriod, c.DrillOptions, true);
                d.Eq(Str(sc, "drillAllLabel"), c.DrillAllLabel, name + " drill all label");
                d.True(sc.GetProperty("viewModeVisible").GetBoolean() == c.ViewModeVisible, name + " viewModeVisible");

                var v = sc.GetProperty("view");
                if (v.GetProperty("message").ValueKind == JsonValueKind.String)
                {
                    d.Eq(v.GetProperty("message").GetString(), dto.Message, name + " message");
                    continue;
                }
                d.True(dto.Message == null, name + " unexpected message " + dto.Message);
                d.Eq(Str(v, "warning"), dto.CoverageWarning, name + " warning");
                d.Eq(Str(v, "quartileTitle"), dto.QuartileTitle, name + " quartileTitle");
                d.Eq(Str(v, "coverageTitle"), dto.CoverageTitle, name + " coverageTitle");
                d.Eq(Str(v, "breakdownHeading"), dto.Breakdown.Heading, name + " breakdownHeading");
                d.Seq(StrList(v.GetProperty("breakdownTitles")), dto.Breakdown.Tables.Select(t => t.Title), name + " breakdownTitles");
                d.Eq(Str(v, "footer"), dto.FooterNote, name + " footer");
                d.True(v.GetProperty("monthNote").GetBoolean() == dto.ShowMonthMappingNote, name + " monthNote");

                // composition chart
                var rects = v.GetProperty("svgRects").EnumerateArray().ToList();
                var segs = dto.Composition.SelectMany(b => b.Segments).ToList();
                d.True(rects.Count == segs.Count, $"{name} rect count {rects.Count} vs {segs.Count}");
                for (var i = 0; i < Math.Min(rects.Count, segs.Count); i++)
                {
                    d.Eq(rects[i].GetProperty("fill").GetString(), segs[i].Color, $"{name} rect {i} fill");
                    d.Num(rects[i].GetProperty("h").GetDouble(), segs[i].Share * 234, $"{name} rect {i} h");
                }
                var texts = new List<string>();
                foreach (var b in dto.Composition)
                {
                    texts.AddRange(b.Segments.Where(s => s.Label != null).Select(s => s.Label!));
                    texts.Add(b.Period);
                }
                d.Seq(StrList(v.GetProperty("svgTexts")), texts, name + " svg texts");

                // coverage bars
                var cov = v.GetProperty("coverage").EnumerateArray().ToList();
                d.True(cov.Count == dto.Coverage.Count, name + " coverage count");
                for (var i = 0; i < Math.Min(cov.Count, dto.Coverage.Count); i++)
                {
                    d.Eq(cov[i].GetProperty("period").GetString(), dto.Coverage[i].Period, $"{name} cov {i} period");
                    d.Num(cov[i].GetProperty("width").GetDouble(), dto.Coverage[i].Pct, $"{name} cov {i} width");
                    d.Eq(cov[i].GetProperty("color").GetString(), dto.Coverage[i].Color, $"{name} cov {i} color");
                    d.Eq(cov[i].GetProperty("text").GetString(), dto.Coverage[i].Text, $"{name} cov {i} text");
                }

                // group cards
                d.Seq(StrList(v.GetProperty("cards")), new[] { CardText(dto.BeforeCard), CardText(dto.WithCard) }, name + " cards");

                // tables: quartile %, breakdown tables, ledger
                var tables = v.GetProperty("tables").EnumerateArray().ToList();
                var mine = new List<List<List<(string Text, string? Color)>>> { QuartileTable(dto) };
                mine.AddRange(dto.Breakdown.Tables.Select(BreakdownTable));
                mine.Add(LedgerTable(dto));
                d.True(tables.Count == mine.Count, $"{name} table count {tables.Count} vs {mine.Count}");
                for (var t = 0; t < Math.Min(tables.Count, mine.Count); t++)
                    CompareTable(d, tables[t], mine[t], $"{name} table {t}");
            }
            d.Done();
        }

        private static void CompareTable(Diff d, JsonElement exp, List<List<(string Text, string? Color)>> act, string where)
        {
            var rows = exp.EnumerateArray().ToList();
            d.True(rows.Count == act.Count, $"{where} row count {rows.Count} vs {act.Count}");
            for (var r = 0; r < Math.Min(rows.Count, act.Count); r++)
            {
                var cells = rows[r].EnumerateArray().ToList();
                d.True(cells.Count == act[r].Count, $"{where} row {r} cell count");
                for (var c = 0; c < Math.Min(cells.Count, act[r].Count); c++)
                {
                    d.Eq(cells[c].GetProperty("text").GetString(), act[r][c].Text, $"{where} r{r} c{c} text");
                    d.Eq(Str(cells[c], "color"), act[r][c].Color, $"{where} r{r} c{c} color");
                }
            }
        }

        private static string CardText(GrcGroupCardDto c) => c.IsEmpty
            ? c.Title + c.EmptyText
            : $"{c.Title} Total GRC Qty: {c.TrueTotalText} · Multi Concept GRC Qty: {c.TotalText} {c.CoverageText} " +
              string.Concat(c.Buckets.Select(b => b.Bucket + b.Text));

        private static List<(string, string?)> Header(IEnumerable<string> h) => h.Select(x => (x, (string?)null)).ToList();

        private static List<List<(string, string?)>> QuartileTable(GrcImpactDto dto)
        {
            var q = dto.QuartileTable;
            var t = new List<List<(string, string?)>> { Header(new[] { q.PeriodHeader }.Concat(q.Headers)) };
            foreach (var r in q.Rows.Append(q.Total))
            {
                var row = new List<(string, string?)> { (r.Period, null) };
                row.AddRange(r.Cells.Select(c => (c.Text, (string?)c.Color)));
                row.Add((r.WrongValueText, null));
                t.Add(row);
            }
            return t;
        }

        private static List<List<(string, string?)>> BreakdownTable(GrcBreakdownTableDto b)
        {
            var t = new List<List<(string, string?)>> { Header(b.Headers) };
            foreach (var r in b.Rows.Append(b.Total))
                t.Add(new List<(string, string?)>
                {
                    (r.Key, null), (r.TrueTotalText, null), (r.TotalText, null), (r.JudgeableText, null), (r.WrongText, null),
                    (r.WrongValueText, null), (r.CoverageText, null), (r.RateText, r.RateColor), (r.RateOfMcText, r.RateColor),
                });
            return t;
        }

        private static List<List<(string, string?)>> LedgerTable(GrcImpactDto dto)
        {
            var l = dto.Ledger;
            var t = new List<List<(string, string?)>> { Header(l.Headers) };
            foreach (var r in l.Rows.Append(l.Total))
                t.Add(new List<(string, string?)>
                {
                    (r.Period, null), (r.TrueTotalText, null), (r.TotalText, null), (r.CoverageText, r.CoverageColor),
                    (r.JudgeableText, null), (r.WrongText, null), (r.RateText, null), (r.WrongValueText, null),
                });
            return t;
        }

        // ── Raw back-test aggregates (_computeGRCBacktest) ────────────────
        [Fact]
        public void Compute_aggregates_match_original()
        {
            var d = new Diff();
            var impact = _f.E.GetProperty("impact").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
            var seasons = GrcTestServices.Seasons();
            foreach (var c in _f.E.GetProperty("compute").EnumerateArray())
            {
                var name = c.GetProperty("name").GetString()!;
                var sc = impact[name];
                var data = _f.Data(sc.GetProperty("ds").GetString()!, ParamsOf(sc));
                var q = Request(sc.GetProperty("req"));
                var sel = seasons.Resolve(data);
                var engine = new GrcBacktestEngine(data, sel.SeasonCurrent, sel.SeasonFallback, Str(sc, "asOf"), 0);
                var bt = engine.Compute(q.Division, q.CatHead, q.ViewMode, q.Brand)!;
                var e = c.GetProperty("bt");
                d.Seq(StrList(e.GetProperty("monthsPresent")), bt.MonthsPresent, name + " monthsPresent");
                d.Eq(e.GetProperty("periodType").GetString(), bt.PeriodType, name + " periodType");
                d.Seq(StrList(e.GetProperty("allDivisions")), bt.AllDivisions, name + " allDivisions");
                d.Seq(StrList(e.GetProperty("allCatHeads")), bt.AllCatHeads, name + " allCatHeads");
                d.Seq(StrList(e.GetProperty("allBrands")), bt.AllBrands, name + " allBrands");
                d.True(e.GetProperty("flatRowCount").GetInt32() == bt.FlatRows.Count, name + " flatRowCount");
                foreach (var (prop, map) in new[] { ("perMonth", bt.PerMonth), ("perDivision", bt.PerDivision), ("perCatHead", bt.PerCatHead),
                             ("perBrand", bt.PerBrand), ("perMonthDivision", bt.PerMonthDivision), ("perMonthCatHead", bt.PerMonthCatHead) })
                {
                    var em = e.GetProperty(prop);
                    d.Seq(em.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal), map.Keys.OrderBy(x => x, StringComparer.Ordinal), $"{name} {prop} keys");
                    foreach (var p in em.EnumerateObject())
                    {
                        if (!map.TryGetValue(p.Name, out var a)) continue;
                        var w = $"{name} {prop}[{p.Name}]";
                        d.Num(p.Value.GetProperty("total").GetDouble(), a.Total, w + ".total");
                        d.Num(p.Value.GetProperty("judgeable").GetDouble(), a.Judgeable, w + ".judgeable");
                        d.Num(p.Value.GetProperty("value").GetDouble(), a.Value, w + ".value");
                        d.Num(p.Value.GetProperty("wrongValue").GetDouble(), a.WrongValue, w + ".wrongValue");
                        foreach (var b in p.Value.GetProperty("buckets").EnumerateObject())
                            d.Num(b.Value.GetDouble(), a.Bucket(b.Name), $"{w}.buckets[{b.Name}]");
                        d.True(p.Value.GetProperty("buckets").EnumerateObject().Count() == a.Buckets.Count, w + " bucket count");
                    }
                }
                foreach (var (prop, map) in new[] { ("trueTotalByMonth", bt.TrueTotalByMonth), ("trueTotalByDivision", bt.TrueTotalByDivision),
                             ("trueTotalByCatHead", bt.TrueTotalByCatHead), ("trueTotalByBrand", bt.TrueTotalByBrand),
                             ("trueTotalByMonthDivision", bt.TrueTotalByMonthDivision), ("trueTotalByMonthCatHead", bt.TrueTotalByMonthCatHead) })
                {
                    var em = e.GetProperty(prop);
                    d.True(em.EnumerateObject().Count() == map.Count, $"{name} {prop} count");
                    foreach (var p in em.EnumerateObject())
                        d.Num(p.Value.GetDouble(), map.GetValueOrDefault(p.Name, double.NaN), $"{name} {prop}[{p.Name}]");
                }
            }
            d.Done();
        }

        // ── Article Trace ──────────────────────────────────────────────────
        [Fact]
        public void Article_trace_matches_original()
        {
            var d = new Diff();
            var svc = GrcTestServices.Grc();
            var impact = _f.E.GetProperty("impact").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
            foreach (var t in _f.E.GetProperty("trace").EnumerateArray())
            {
                var scName = t.GetProperty("scenario").GetString()!;
                var sc = impact[scName];
                var data = _f.Data(sc.GetProperty("ds").GetString()!, ParamsOf(sc));
                var gm = t.GetProperty("gm").GetString()!;
                var name = $"{scName} trace «{gm}»";
                var dto = svc.Trace(data, gm, Str(t, "asOf"));
                var res = t.GetProperty("result");
                if (res.ValueKind == JsonValueKind.Null)
                {
                    d.True(dto.IsBlank, name + " blank");
                    continue;
                }
                if (res.TryGetProperty("error", out var err))
                {
                    d.Eq(err.GetString(), dto.Error, name + " error");
                    d.Eq(Str(t, "error"), dto.Error, name + " rendered error");
                    continue;
                }
                d.True(dto.Error == null && !dto.IsBlank, name + " ok");
                d.Eq(Str(res, "gm"), dto.Gm, name + " gm");
                d.Eq(Str(res, "division"), dto.Division, name + " division");
                d.Eq(Str(res, "mcCd"), dto.McCd, name + " mcCd");
                d.Eq(Str(res, "mcDesc"), dto.McDesc, name + " mcDesc");
                d.Eq(Str(res, "mrp"), dto.Mrp, name + " mrp");
                d.Eq(Str(res, "brand"), dto.Brand, name + " brand");
                d.Eq(Str(res, "flag"), dto.Flag, name + " flag");
                d.True(res.GetProperty("isSingleConcept").GetBoolean() == dto.IsSingleConcept, name + " single");
                var rows = res.GetProperty("weekRows").EnumerateArray().ToList();
                d.True(rows.Count == dto.WeekRows.Count, name + " row count");
                for (var i = 0; i < Math.Min(rows.Count, dto.WeekRows.Count); i++)
                {
                    var e = rows[i]; var a = dto.WeekRows[i]; var w = $"{name} row {i}";
                    d.Eq(Str(e, "period"), a.Period, w + " period");
                    d.Eq(Str(e, "colour"), a.Colour, w + " colour");
                    d.Num(e.GetProperty("qty").GetDouble(), a.Qty, w + " qty");
                    var ev = e.TryGetProperty("value", out var vv) && vv.ValueKind == JsonValueKind.Number ? vv.GetDouble() : (double?)null;
                    d.True(ev == a.Value, w + " value");
                    d.Eq(Str(e, "bucket"), a.Bucket, w + " bucket");
                    d.Eq(Str(e, "detail"), a.Detail, w + " detail");
                    foreach (var (p, val) in new[] { ("weeksElapsed", a.WeeksElapsed), ("estimatedWof", a.EstimatedWof), ("wofNow", a.WofNow),
                                 ("wofGate", a.WofGate), ("poolSize", a.PoolSize), ("peerMin", a.PeerMin), ("pdstr", a.Pdstr), ("wof", a.Wof) })
                    {
                        if (e.TryGetProperty(p, out var x)) d.True(val != null && x.GetDouble() == val, $"{w} {p} {x} vs {val}");
                        else d.True(val == null, $"{w} {p} should be absent");
                    }
                }
                // rendered table: Week | Colour | GRC Qty | Bucket (pill colour) | Reasoning
                var html = t.GetProperty("html");
                var table = html.GetProperty("tables")[0].EnumerateArray().Skip(1).ToList();
                for (var i = 0; i < Math.Min(table.Count, dto.WeekRows.Count); i++)
                {
                    var cells = table[i].EnumerateArray().ToList(); var a = dto.WeekRows[i]; var w = $"{name} html row {i}";
                    d.Eq(cells[0].GetProperty("text").GetString(), a.Period, w + " period");
                    d.Eq(cells[1].GetProperty("text").GetString(), a.Colour, w + " colour");
                    d.Eq(cells[2].GetProperty("text").GetString(), a.QtyText, w + " qty");
                    d.Eq(cells[3].GetProperty("text").GetString(), a.Bucket, w + " bucket");
                    d.Eq(Str(cells[3], "color"), a.BucketColor, w + " bucket colour");
                    d.Eq(cells[4].GetProperty("text").GetString(), a.Extra, w + " extra");
                }
                var summary = $"GM {dto.Gm} Brand {dto.Brand} Division {dto.Division} MC {dto.McCd} — {dto.McDesc} MRP ₹{dto.Mrp} Flag {dto.FlagText}"
                              + (dto.SingleConceptNote != null ? " " + dto.SingleConceptNote : "") + " Week Colour GRC Qty Bucket Reasoning";
                summary = System.Text.RegularExpressions.Regex.Replace(summary, @"\s+", " "); // HTML whitespace collapse
                d.True(html.GetProperty("text").GetString()!.StartsWith(summary, StringComparison.Ordinal), name + " summary: " + summary);
            }
            d.Done();
        }

        // ── Row-detail export ──────────────────────────────────────────────
        [Fact]
        public void Row_detail_export_matches_original()
        {
            var d = new Diff();
            var svc = GrcTestServices.Grc();
            var impact = _f.E.GetProperty("impact").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
            foreach (var x in _f.E.GetProperty("export").EnumerateArray())
            {
                var scName = x.GetProperty("scenario").GetString()!;
                var sc = impact[scName];
                var data = _f.Data(sc.GetProperty("ds").GetString()!, ParamsOf(sc));
                var dto = svc.GetRowDetail(data, Str(x, "asOf"))!;
                d.Eq(Str(x, "toast"), dto.Message, scName + " toast");
                d.Eq(Str(x, "sheet"), dto.SheetName, scName + " sheet");
                d.True(dto.FileName.StartsWith(Str(x, "fileNamePrefix")!) && dto.FileName == "GRC_Row_Detail_2026-10-14.xlsx", scName + " file " + dto.FileName);
                foreach (var col in x.GetProperty("cols").EnumerateArray()) d.Num(col.GetProperty("wch").GetDouble(), dto.ColumnWidth, scName + " width");
                d.True(x.GetProperty("autofilter").GetProperty("e").GetProperty("r").GetInt32() == dto.Rows.Count
                       && x.GetProperty("autofilter").GetProperty("e").GetProperty("c").GetInt32() == dto.Headers.Count - 1, scName + " autofilter");
                var rows = x.GetProperty("rows").EnumerateArray().ToList();
                d.Seq(StrList(rows[0]), dto.Headers, scName + " headers");
                d.True(rows.Count - 1 == dto.Rows.Count, scName + " row count");
                for (var r = 1; r < Math.Min(rows.Count, dto.Rows.Count + 1); r++)
                {
                    var cells = rows[r].EnumerateArray().ToList();
                    var act = dto.Rows[r - 1];
                    for (var c = 0; c < cells.Count; c++)
                    {
                        var e = cells[c]; var a = act[c]; var w = $"{scName} row {r} col {dto.Headers[c]}";
                        if (e.ValueKind == JsonValueKind.Number) d.True(a is double ad && ad == e.GetDouble(), $"{w}: expected {e} got {a}");
                        else d.True(a is string s && s == e.GetString(), $"{w}: expected «{e}» got «{a}» ({a?.GetType().Name})");
                    }
                }
            }
            d.Done();
        }

        // ── Season Selection ───────────────────────────────────────────────
        [Fact]
        public void Season_selection_matches_original()
        {
            var d = new Diff();
            var seasons = GrcTestServices.Seasons();
            var baseData = _f.Data("week");
            foreach (var s in _f.E.GetProperty("seasons").EnumerateArray())
            {
                var name = s.GetProperty("name").GetString()!;
                IReadOnlyList<SheetRow>? art = name == "no-artmaster" ? null
                    : name == "single-season" ? GrcParityFixture.Rows(JsonDocument.Parse("[{\"Season\":\"aw-2027\"},{\"SEASON\":\"AW27\"},{\"season\":\"junk\"},{\"Season\":\"SS 99\"},{\"Season\":\"AW026\"}]").RootElement)
                    : name == "no-season-col" ? GrcParityFixture.Rows(JsonDocument.Parse("[{\"X\":1}]").RootElement)
                    : baseData.ArtMaster;
                var files = new Dictionary<string, IReadOnlyList<SheetRow>?> { ["artmaster"] = art };
                var data = GrcParityFixture.DataFrom(files, _f.Params(s.GetProperty("params")));
                d.Seq(StrList(s.GetProperty("sorted")), SeasonLogic.AllSeasonsSorted(data), name + " sorted");
                var r = seasons.Resolve(data);
                d.Seq(StrList(s.GetProperty("sorted")), r.Seasons, name + " dto seasons");
                d.Seq(StrList(s.GetProperty("current")), r.SeasonCurrent, name + " current");
                d.Seq(StrList(s.GetProperty("fallback")), r.SeasonFallback, name + " fallback");
                d.Eq(Str(s, "message"), r.Message, name + " message");
            }

            // pill clicks in sequence, each saved back to the params (as S.params did)
            var p = _f.Params(null);
            var filesAm = new Dictionary<string, IReadOnlyList<SheetRow>?> { ["artmaster"] = baseData.ArtMaster };
            var cur = seasons.Resolve(GrcParityFixture.DataFrom(filesAm, p));
            p.SeasonCurrent = cur.SeasonCurrent; p.SeasonFallback = cur.SeasonFallback;
            foreach (var t in _f.E.GetProperty("seasonToggles").EnumerateArray())
            {
                var r = seasons.Toggle(GrcParityFixture.DataFrom(filesAm, p), Str(t, "group")!, Str(t, "season")!);
                d.Seq(StrList(t.GetProperty("current")), r.SeasonCurrent, $"toggle {t} current");
                d.Seq(StrList(t.GetProperty("fallback")), r.SeasonFallback, $"toggle {t} fallback");
                p = p.Clone(); p.SeasonCurrent = r.SeasonCurrent; p.SeasonFallback = r.SeasonFallback;
            }
            foreach (var k in _f.E.GetProperty("seasonKeys").EnumerateArray())
            {
                var exp = k.GetProperty("key");
                var act = SeasonLogic.SortKey(k.GetProperty("s").GetString());
                d.True(exp.ValueKind == JsonValueKind.Null ? act == null : act == exp.GetDouble(), $"season key {k}: got {act}");
            }
            d.Done();
        }

        // ── Period math ───────────────────────────────────────────────────
        [Fact]
        public void Period_math_matches_original()
        {
            var d = new Diff();
            var pm = _f.E.GetProperty("periodMath");
            foreach (var x in pm.GetProperty("isoWeek").EnumerateArray())
                d.Num(x.GetProperty("ms").GetDouble(), GrcPeriodMath.IsoWeekToDate(x.GetProperty("w").GetDouble(), x.GetProperty("y").GetDouble()), $"iso {x}");
            foreach (var x in pm.GetProperty("weekToMonth").EnumerateArray())
                d.Eq(Str(x, "m"), GrcPeriodMath.WeekPeriodToMonthLabel(Str(x, "p")!), $"weekToMonth {x}");
            foreach (var x in pm.GetProperty("elapsed").EnumerateArray())
            {
                var asOfMs = GrcPeriodMath.AsOfDate(Str(x, "asOf"), Str(x, "type"), 0);
                d.Num(x.GetProperty("asOfMs").GetDouble(), asOfMs, $"asOf {x}");
                d.Num(x.GetProperty("weeks").GetDouble(), GrcPeriodMath.WeeksElapsedSincePeriod(Str(x, "p")!, Str(x, "type")!, asOfMs), $"elapsed {x}");
            }
            d.Done();
        }

        // ── No GRC / reset filters ─────────────────────────────────────────
        [Fact]
        public void No_grc_and_reset_filters_match_original()
        {
            var svc = GrcTestServices.Grc();
            var week = _f.Data("week");
            var files = new Dictionary<string, IReadOnlyList<SheetRow>?> { ["artmaster"] = week.ArtMaster, ["bsr"] = week.Bsr };
            var data = GrcParityFixture.DataFrom(files, _f.Params(null));
            var no = _f.E.GetProperty("noGrc");
            var sel = GrcTestServices.Seasons().Resolve(data);
            Assert.Null(new GrcBacktestEngine(data, sel.SeasonCurrent, sel.SeasonFallback, null, 0).Compute(null, null, "native", null));
            Assert.Equal(no.GetProperty("trace").GetProperty("error").GetString(), svc.Trace(data, "1110064000", null).Error);
            Assert.Null(svc.GetRowDetail(data, null));
            Assert.Equal("No GRC file loaded", no.GetProperty("exportToast").GetString());
            var dto = svc.Compute(data, new GrcImpactRequest());
            Assert.False(dto.HasGrcData);
            Assert.Equal("No GRC data to show.", dto.Message);
            Assert.Contains(dto.Message!, no.GetProperty("body").GetString());

            // _grcResetFilters: all four filters back to "" and the full screen re-rendered
            var reset = _f.E.GetProperty("resetFilters");
            var r = svc.Compute(week, new GrcImpactRequest());
            Assert.Equal(reset.GetProperty("division").GetString(), r.Controls.Division);
            Assert.Equal(reset.GetProperty("drill").GetString(), r.Controls.DrillPeriod);
            Assert.Equal(reset.GetProperty("view").GetInt32(), 2 + r.Breakdown.Tables.Count);
        }
    }
}

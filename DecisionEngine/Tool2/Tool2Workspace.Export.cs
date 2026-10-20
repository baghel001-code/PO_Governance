using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DecisionEngine.Common;
using DecisionEngine.Model;
using DecisionEngine.Results.Tool2;

namespace DecisionEngine.Tool2
{
    /// <summary>
    /// The exports of the assessment — ports of index.html <c>exportAssCSV</c> (line 7663) and
    /// <c>exportAssXLSX</c> (line 7731, incl. <c>_initialsFor</c>, <c>_safeSheetName</c>,
    /// <c>_buildSingleConceptUniverseForCatHead</c>, <c>_monthLabelHuman</c>, <c>_buildColourRecTable</c>,
    /// <c>_colorForCatHead</c>, BUCKET_ORDER / BUCKET_DESC / TYPE_COLORS / XLS_DECISION_COLORS). The workbook is
    /// returned as a <see cref="Tool2WorkbookDto"/> (cells + styles); Infrastructure writes the .xlsx
    /// (no gridlines — <c>_writeXlsxNoGridlines</c>).
    /// </summary>
    public sealed partial class Tool2Workspace
    {
        public const string SummarySheet = "Summary Dashboard";
        public const string AllSheet = "All Decisions";

        private static readonly Tool2BucketDto[] BucketOrder =
        {
            new() { Type = "WATCHLIST", Sub = "BoBs", Code = "WL-BoBs" },
            new() { Type = "WATCHLIST", Sub = "True Repeat", Code = "WL-TR" },
            new() { Type = "WATCHLIST", Sub = "Single Concept", Code = "WL-SC" },
            new() { Type = "REDUCE/CANCEL", Sub = "Single Concept", Code = "RC-SC" },
            new() { Type = "REDUCE/CANCEL", Sub = "Multi Concept", Code = "RC-MC" }
        };

        public static readonly IReadOnlyDictionary<string, string> BucketDesc = new Dictionary<string, string>
        {
            ["WL-BoBs"] = "A new colourway is being added to an existing design, and this colourway is a slow or average seller — Category Head review needed before approving.",
            ["WL-TR"] = "Borderline case of a slow seller for an existing style — needs Category Head judgment on whether to retain or cut.",
            ["WL-SC"] = "Colour Mix% within this MC×MRP is below 2%, yet a higher quantity has been ordered — Category Head review needed to confirm the order makes sense.",
            ["RC-SC"] = "A colour's stock cover (STR) is too high relative to its current rate of sale — the system is recommending a Reduce or Cancel on the quantity.",
            ["RC-MC"] = "This option is a slow mover relative to its peers in the same category — recommended for Cancel."
        };

        private static readonly string[] CatHeadPalette = { "1A4FAF", "00873E", "B45309", "7C3AED", "DC2626", "0E7490", "4D7C0F", "BE185D" };

        // ── CSV (exportAssCSV) ─────────────────────────────────────────────

        public string Csv()
        {
            if (Results.Count == 0) throw new EngineValidationException("No assessment results to export");
            var sb = new StringBuilder();
            sb.Append("PO Number,Generic Material,Colour,Brand Type,Division,MC Desc,MRP,Delivery Month,Released Qty,PO Value (₹),Engine,STR (days),Decision,Max Retain Qty,Reason,STR Caution,")
              .Append("Quartile (Tool 1 Peer Rank),Percentile,PDSTR %/day,Peer Avg %/day,Peer Pool Size,BSR Source,Recommended Action (Engine A),")
              .Append("Option 1 (Best),Option 2,Option 3,Option 4\n");
            string Q(string s) => "\"" + s.Replace("\"", "'") + "\"";
            string Caution(Tool2LineResult r) => r.StrCaution?.Items is { Count: > 0 } items
                ? string.Join("; ", items.Select(it => it.Label + " STR=" + N(it.Str) + "d (threshold " + N(r.StrCaution.EffT2) + "d)")) : "";
            var lines = Results.Select(r =>
            {
                var opts = RankedOptionsAny(r, 4);
                string O(int i) => i < opts.Count ? opts[i] : "";
                var a = r.Engine == "A";
                return string.Join(",", new[]
                {
                    r.PoNum, r.Gm, r.Colour, r.BrandOrNonPl, r.Div, "\"" + r.McDesc + "\"", r.Mrp, r.Dm, N(r.RelQty), r.PoValue != null ? N(r.PoValue.Value) : "",
                    r.Engine == "B" ? "Single Concept MRP" : "Multi Concept MRP",
                    r.ArtStr != null ? N(Js.Round(r.ArtStr.Value)) : "",
                    r.Decision, r.RecQty > 0 ? N(r.RecQty) : "",
                    Q(r.DecisionReason ?? ""), Q(Caution(r)),
                    a ? Or(r.Quartile, "—") : "—",
                    a && r.Percentile != null ? N(r.Percentile.Value) : "—",
                    a && r.Pdstr != null ? Pct1(r.Pdstr.Value) : "—",
                    a && r.PeerAvg != null ? Pct1(r.PeerAvg.Value) : "—",
                    a && r.CleanPool != null ? r.CleanPool.Count.ToString(CultureInfo.InvariantCulture) : "—",
                    a ? Or(r.BsrLabel, "—") : "—",
                    Q(a ? RecommendedActionA(r) : ""),
                    Q(O(0)), Q(O(1)), Q(O(2)), Q(O(3))
                });
            });
            sb.Append(string.Join("\n", lines));
            return sb.ToString();
        }

        public static string FileDate(DateTimeOffset now) => now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // ── Excel (exportAssXLSX) ──────────────────────────────────────────

        /// <summary>A cell style the way xlsx-js-style holds it (Object.assign merges whole font / fill / alignment / border).</summary>
        private sealed class JsStyle
        {
            public (string? Color, bool? Bold, bool? Italic, bool? Underline, double? Sz)? Font;
            public string? Fill;
            public (string? H, string? V, bool? Wrap, int? Indent)? Align;
            public string? Border;
            public bool HasFont, HasFill, HasAlign, HasBorder;

            public static JsStyle Make((string? Color, bool? Bold, bool? Italic, bool? Underline, double? Sz)? font = null, string? fill = null,
                (string? H, string? V, bool? Wrap, int? Indent)? align = null, string? border = null)
                => new() { Font = font, HasFont = font != null, Fill = fill, HasFill = fill != null, Align = align, HasAlign = align != null, Border = border, HasBorder = border != null };

            /// <summary>Object.assign(this, other) for the top-level keys present in other.</summary>
            public JsStyle Assign(JsStyle o)
            {
                var r = (JsStyle)MemberwiseClone();
                if (o.HasFont) { r.Font = o.Font; r.HasFont = true; }
                if (o.HasFill) { r.Fill = o.Fill; r.HasFill = true; }
                if (o.HasAlign) { r.Align = o.Align; r.HasAlign = true; }
                if (o.HasBorder) { r.Border = o.Border; r.HasBorder = true; }
                return r;
            }
        }

        private sealed class Sheet
        {
            public string Name = "";
            public List<List<object?>> Rows = new();
            public Dictionary<(int R, int C), JsStyle> S = new();
            public Dictionary<(int R, int C), string> Z = new();
            public Dictionary<(int R, int C), (string Target, string Tip)> L = new();
            public List<Tool2ColumnDto>? Cols;
            public List<string>? Merges;
            public string? AutoFilter;

            public object? Cell(int r, int c) => r < Rows.Count && c < Rows[r].Count ? Rows[r][c] : null;
            public bool Exists(int r, int c) => Cell(r, c) != null;
            public JsStyle? Get(int r, int c) => S.TryGetValue((r, c), out var s) ? s : null;
            public void Set(int r, int c, JsStyle s) => S[(r, c)] = s;
            public void Put(int r, int c, object? v)
            {
                while (Rows.Count <= r) Rows.Add(new List<object?>());
                var row = Rows[r];
                while (row.Count <= c) row.Add(null);
                row[c] = v;
            }
        }

        private const string Thin = "D3D9E3";
        private static (string?, bool?, bool?, bool?, double?) F(string? color = null, bool? bold = null, double? sz = null, bool? italic = null, bool? underline = null)
            => (color, bold, italic, underline, sz);
        private static (string?, string?, bool?, int?) A(string? h = null, string? v = null, bool? wrap = null, int? indent = null) => (h, v, wrap, indent);

        private static string EncodeCol(int c)
        {
            var s = ""; c++;
            while (c > 0) { var m = (c - 1) % 26; s = (char)('A' + m) + s; c = (c - 1) / 26; }
            return s;
        }

        public static string Addr(int r, int c) => EncodeCol(c) + (r + 1).ToString(CultureInfo.InvariantCulture);

        private static string InitialsFor(string name)
        {
            if (name == "Unassigned") return "UNA";
            var parts = Regex.Split(name, @"\s+").Where(p => p.Length > 0);
            var s = string.Concat(parts.Select(p => p[..1].ToUpperInvariant()));
            if (s.Length > 3) s = s[..3];
            return s.Length > 0 ? s : "XX";
        }

        private static readonly Regex BadSheetChars = new(@"[\[\]:\*\?/\\]", RegexOptions.Compiled);

        private static string SafeSheetName(string b, HashSet<string> used)
        {
            var name = BadSheetChars.Replace(b, "-");
            if (name.Length > 31) name = name[..31];
            if (used.Add(name)) return name;
            int i = 2; var trimmed = b.Length > 28 ? b[..28] : b;
            string Cut(string s) => s.Length > 31 ? s[..31] : s;
            while (used.Contains(Cut(trimmed + "-" + i))) i++;
            name = Cut(trimmed + "-" + i);
            used.Add(name);
            return name;
        }

        /// <summary>Port of <c>_monthLabelHuman(m)</c>: "07.2026" → "July'2026".</summary>
        public static string MonthLabelHuman(string m)
        {
            string[] names = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
            var parts = m.Split('.');
            var mm = MonthMath.ParseInt(parts[0]);
            var yyyy = parts.Length > 1 ? parts[1] : null;
            if (double.IsNaN(mm) || mm == 0 || mm < 1 || mm > 12 || string.IsNullOrEmpty(yyyy)) return m;
            return names[(int)mm - 1] + "'" + yyyy;
        }

        private sealed record ColourRecTable(List<object?> Header, List<List<object?>> DataRows, double Eff, int ColCount);

        /// <summary>Port of <c>_buildColourRecTable(mc, mrp, brand, flag)</c>.</summary>
        private ColourRecTable? BuildColourRecTable(string mc, string mrp, string brand, string flag)
        {
            var allMonths = Rec.BuildMonths(Data, mc, mrp, brand, flag);
            int nm = Ctx.M, ny = Ctx.Y;
            var months = allMonths.Where(m =>
            {
                var p = m.Split('.');
                var mm = MonthMath.ParseInt(p[0]); var my = p.Length > 1 ? MonthMath.ParseInt(p[1]) : double.NaN;
                return my * 12 + mm >= ny * 12 + nm;
            }).Take(4).ToList();
            if (months.Count == 0) return null;
            var res = Rec.RunBCached(Ctx, mc, mrp, brand, flag, months, RunCache);
            if (res == null || res.Rows.Count == 0) return null;
            var rows = res.Rows.Where(r => !r.WatchList).ToList();
            if (rows.Count == 0) return null;
            var header = new List<object?> { "Rank", "Colour", "Stock", "Sale (Last 45 days)", "Stock Mix%", "Sale Mix%", "STR ROS" };
            foreach (var m in months) { var ml = MonthLabelHuman(m); header.Add(ml + " — Pending PO"); header.Add(ml + " — Max Permissible Qty"); }
            var data = rows.Select((r, i) =>
            {
                var row = new List<object?> { (double)(i + 1), r.Col.ToUpperInvariant(), r.Stk, r.S45, N(r.StkMix) + "%", N(r.Mix) + "%", r.StrRos != null ? N(r.StrRos.Value) + "d" : "—" };
                foreach (var md in r.MData) { row.Add(md.Pend); row.Add(md.Rec > 0 ? md.Rec : md.Action); }
                return row;
            }).ToList();
            return new ColourRecTable(header, data, res.Eff, rows.Count);
        }

        private sealed record ScBucket(string Brand, string McCd, string Mrp, string Flag, string McDesc);

        /// <summary>Port of <c>_buildSingleConceptUniverseForCatHead(targetName)</c>.</summary>
        private List<ScBucket> SingleConceptUniverseForCatHead(string targetName)
        {
            var mapped = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in Data.CatHeadMap ?? Array.Empty<SheetRow>())
            {
                var email = CatHeadEmailOfRow(r);
                if (email.Length == 0 || CatHeadDisplayName(email) != targetName) continue;
                mapped.Add(CatHeadKeyOfRow(r));
            }
            if (mapped.Count == 0) return new List<ScBucket>();
            var um = new Dictionary<string, SheetRow>(StringComparer.Ordinal);
            foreach (var r in Data.Um ?? Array.Empty<SheetRow>())
            {
                var k = r.Col("Generic Material", "generic material", "GENERIC MATERIAL").Trim();
                if (k.Length > 0) um[k] = r;
            }
            var buckets = new Dictionary<string, ScBucket>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var r in Data.ArtMaster ?? Array.Empty<SheetRow>())
            {
                var gm = r.Col("Generic Material", "generic material", "GENERIC MATERIAL").Trim();
                if (gm.Length == 0 || !um.TryGetValue(gm, out var umRow)) continue;
                var brand = r.Col("Brand Type", "BRAND TYPE", "brand type");
                var mcCd = r.Col("MC CD", "MC_CD", "mc cd", "MC Code");
                var mrp = r.Col("MRP", "mrp", "MRP value", "MRP Value");
                var flag = r.Col("Flag (BOGO/NON-BOGO)", "BOGO/NON-BOGO", "Flag", "flag", "Article Type", "Bogo/Non-Bogo");
                var umMrp = umRow.Col("MRP", "mrp", "MRP value", "MRP Value");
                var umFlag = umRow.Col("Flag (BOGO/NON-BOGO)", "BOGO/NON-BOGO", "Flag", "flag", "Article Type", "Bogo/Non-Bogo");
                if (Js.N(mrp) != Js.N(umMrp) || Js.N(flag) != Js.N(umFlag)) continue;
                var key3 = Js.N(brand) + "|" + Js.N(mcCd) + "|" + Js.N(mrp);
                if (!mapped.Contains(key3)) continue;
                var key4 = key3 + "|" + Js.N(flag);
                if (!buckets.ContainsKey(key4))
                {
                    buckets[key4] = new ScBucket(brand, mcCd, mrp, flag, r.Col("MC DES", "MC DESC", "MC Desc", "mc desc", "MC Description", "MC Name"));
                    order.Add(key4);
                }
            }
            return order.Select(k => buckets[k]).ToList();
        }

        /// <summary>The en-IN <c>new Date().toLocaleString()</c> of the Summary sheet: "3/10/2026, 12:00:00 pm".</summary>
        public static string LocaleDateTimeIN(DateTimeOffset t)
        {
            var h = t.Hour % 12; if (h == 0) h = 12;
            return t.Day + "/" + t.Month + "/" + t.Year + ", " + h + ":" + t.Minute.ToString("00") + ":" + t.Second.ToString("00") + " " + (t.Hour < 12 ? "am" : "pm");
        }

        /// <summary>
        /// Port of <c>exportAssXLSX()</c> for a Category Head scope (<paramref name="scopeNames"/> empty = everyone).
        /// Throws <see cref="EngineValidationException"/> with the v1.1.4 toast when nothing matches.
        /// </summary>
        public Tool2WorkbookDto BuildWorkbook(IReadOnlyCollection<string>? scopeNames, DateTimeOffset now)
        {
            if (Results.Count == 0) throw new EngineValidationException("No assessment results to export");
            var scope = scopeNames?.Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList() ?? new List<string>();
            var exportRows = scope.Count > 0 ? Results.Where(r => scope.Contains(CatHeadFor(r))).ToList() : Results.ToList();
            if (scope.Count > 0 && exportRows.Count == 0) throw new EngineValidationException("No rows match the selected Category Head(s) — export cancelled.");

            var colorCache = new Dictionary<string, string>(StringComparer.Ordinal);
            string ColorFor(string name)
            {
                if (name == "Unassigned") return "6B7280";
                if (colorCache.TryGetValue(name, out var c)) return c;
                c = CatHeadPalette[colorCache.Count % CatHeadPalette.Length];
                colorCache[name] = c;
                return c;
            }

            // PHASE 1 — groups
            var groups = new Dictionary<string, (string Name, Tool2BucketDto Bucket, List<Tool2LineResult> Rows)>(StringComparer.Ordinal);
            var sheetNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var groupOrder = new List<string>();
            foreach (var r in exportRows)
            {
                var b = BucketFor(r);
                if (b == null) continue;
                var name = CatHeadFor(r);
                var key = name + "|" + b.Code;
                if (!groups.ContainsKey(key)) { groups[key] = (name, b, new List<Tool2LineResult>()); groupOrder.Add(key); }
                groups[key].Rows.Add(r);
            }
            // PHASE 2 — sheet names
            var used = new HashSet<string>(StringComparer.Ordinal) { SummarySheet, AllSheet };
            var allNames = new List<string>();
            foreach (var k in groupOrder) if (!allNames.Contains(groups[k].Name)) allNames.Add(groups[k].Name);
            JsSort.StableSort(allNames, (a, b) => CatHeadSort(a, b));
            foreach (var name in allNames)
            {
                var ini = InitialsFor(name);
                foreach (var b in BucketOrder)
                    if (groups.TryGetValue(name + "|" + b.Code, out var g) && g.Rows.Count > 0)
                        sheetNames[name + "|" + b.Code] = SafeSheetName(ini + "-" + b.Code, used);
            }

            var wbSheets = new List<Sheet>();
            wbSheets.Add(SummaryDashboard(exportRows, groups, allNames, sheetNames, ColorFor, now));

            // PHASE 4 — per-group sheets
            foreach (var name in allNames)
                foreach (var b in BucketOrder)
                {
                    var key = name + "|" + b.Code;
                    if (!groups.TryGetValue(key, out var g) || g.Rows.Count == 0 || !sheetNames.TryGetValue(key, out var sn)) continue;
                    wbSheets.Add(GroupSheet(sn, name, b, g.Rows));
                }

            // All Decisions
            var all = new Sheet { Name = AllSheet };
            all.Rows.Add(new List<object?> { "PO Number", "Generic Material", "Colour", "Brand Type", "Division", "MC Description", "MRP (₹)", "Delivery Month", "Released Qty", "PO Value (₹)",
                "Engine", "Option Type", "STR (days)", "Quartile (Tool 1 Peer Rank)", "Percentile", "PDSTR %/day", "Peer Avg %/day", "Decision", "Decision Reason" });
            foreach (var r in exportRows)
                all.Rows.Add(new List<object?>
                {
                    Or(r.PoNum, "—"), r.Gm, r.Colour, r.BrandOrNonPl, r.Div, r.McDesc, MrpNum(r), r.Dm, r.RelQty, PoValueCell(r),
                    EngLabel(r), Or(r.OptionType, "—"), r.ArtStr != null ? Js.Round(r.ArtStr.Value) : "—",
                    r.Engine == "A" ? Or(r.Quartile, "—") : "—",
                    r.Engine == "A" && r.Percentile != null ? r.Percentile.Value : "—",
                    r.Engine == "A" && r.Pdstr != null ? Js.ParseFloat(Pct1(r.Pdstr.Value)) : "—",
                    r.Engine == "A" && r.PeerAvg != null ? Js.ParseFloat(Pct1(r.PeerAvg.Value)) : "—",
                    r.Decision, r.DecisionReason ?? ""
                });
            all.Cols = new[] { 18, 16, 10, 12, 12, 28, 10, 14, 12, 14, 16, 12, 11, 14, 11, 12, 13, 16, 60 }.Select(w => new Tool2ColumnDto { Width = w }).ToList();
            wbSheets.Add(all);

            var wb = new Tool2WorkbookDto
            {
                FileName = "VMM_RaisedPO_Assessment_" + FileDate(now) + ".xlsx",
                Sheets = wbSheets.Select(ToDto).ToList()
            };
            wb.Message = "Excel exported ✓ — " + wb.Sheets.Count + " sheets" +
                         (scope.Count > 0 ? " (scoped to: " + string.Join(", ", scope) + ")" : " (Summary + one per Category Head × Type + All Decisions)");
            return wb;
        }

        private static string EngLabel(Tool2LineResult r) => r.Engine == "B" ? "Single Concept MRP" : "Multi Concept MRP";
        private static object MrpNum(Tool2LineResult r) => string.IsNullOrEmpty(r.Mrp) ? 0.0 : Js.ParseFloat(r.Mrp);
        private static object PoValueCell(Tool2LineResult r) => r.PoValue != null ? r.PoValue.Value : "";

        private static readonly string Navy = "1F2A44", NavyDark = "16213A", LtGrey = "F4F6F9", White = "FFFFFF";

        private static readonly Dictionary<string, (string Font, string Fill)> DecColors2 = new()
        {
            ["Retain"] = ("1E7B45", "E5F4EA"), ["Watch List"] = ("9A6700", "FFF3CD"), ["Reduce Qty"] = ("B45309", "FEF0DC"),
            ["Cancel"] = ("B3261E", "FDE7E5"), ["Cannot Validate"] = ("555555", "EDEDED")
        };

        private static readonly Dictionary<string, (string Font, string Fill)> TypeColors = new()
        {
            ["WATCHLIST"] = ("92400E", "FFF3CD"), ["REDUCE/CANCEL"] = ("991B1B", "FEE2E2")
        };

        private static readonly Dictionary<string, (string Font, string Fill)> XlsDecisionColors = new()
        {
            ["Retain"] = ("065F46", "D1FAE5"), ["Watch List"] = ("92400E", "FFF3CD"), ["Reduce Qty"] = ("9A3412", "FFEDD5"), ["Cancel"] = ("991B1B", "FEE2E2")
        };

        private Sheet SummaryDashboard(List<Tool2LineResult> exportRows,
            Dictionary<string, (string Name, Tool2BucketDto Bucket, List<Tool2LineResult> Rows)> groups,
            List<string> allNames, Dictionary<string, string> sheetNames, Func<string, string> colorFor, DateTimeOffset now)
        {
            var total = exportRows.Count;
            var decCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var r in exportRows) decCounts[r.Decision] = (decCounts.TryGetValue(r.Decision, out var c) ? c : 0) + 1;
            int DC(string d) => decCounts.TryGetValue(d, out var c) ? c : 0;
            var flagged = exportRows.Where(IsAction).ToList();
            double qtyAtRisk = 0, valueAtRisk = 0;
            foreach (var r in flagged) { qtyAtRisk += r.CutQty; valueAtRisk += ValueAtRisk(r, r.CutQty); }
            string[] decOrder2 = { "Retain", "Watch List", "Reduce Qty", "Cancel", "Cannot Validate" };

            var mcMap = new JsObjectMap<(int Total, int Retain, int Watch, int Cut, double Value)>();
            foreach (var r in exportRows)
            {
                var mc = string.IsNullOrEmpty(r.McDesc) ? "Unclassified" : r.McDesc;
                var v = mcMap.ContainsKey(mc) ? mcMap[mc] : (Total: 0, Retain: 0, Watch: 0, Cut: 0, Value: 0.0);
                v.Total++;
                if (r.Decision == Decisions.Retain) v.Retain++;
                else if (r.Decision == Decisions.WatchList) v.Watch++;
                else if (r.Decision == Decisions.ReduceQty || r.Decision == Decisions.Cancel) { v.Cut++; v.Value += ValueAtRisk(r, r.CutQty); }
                mcMap[mc] = v;
            }
            var mcAll = mcMap.ToList();
            JsSort.StableSort(mcAll, (a, b) => (double)(b.Value.Cut + b.Value.Watch) / b.Value.Total - (double)(a.Value.Cut + a.Value.Watch) / a.Value.Total);
            var mcTop = mcAll.Take(20).ToList();

            var sh = new Sheet { Name = SummarySheet };
            var rows = sh.Rows;
            var styles = new List<(int R, int C, JsStyle S, string? Z)>();
            void Push(IEnumerable<(int, int)> cells, JsStyle s) { foreach (var (r, c) in cells) styles.Add((r, c, s, null)); }
            void SetLastZ(string z) { var l = styles[^1]; styles[^1] = (l.R, l.C, l.S, z); }
            int Section(string text)
            {
                var r = rows.Count; rows.Add(new List<object?> { text });
                Push(new[] { (r, 0) }, JsStyle.Make(F(White, true, 11), Navy));
                return r;
            }

            rows.Add(new List<object?> { "VMM RAISED PO ASSESSMENT — SUMMARY DASHBOARD" });
            Push(new[] { (0, 0) }, JsStyle.Make(F(White, true, 16), Navy, A("left", "center", null, 1)));
            rows.Add(new List<object?> { "Generated " + LocaleDateTimeIN(now) });
            Push(new[] { (1, 0) }, JsStyle.Make(F("666666", null, 9, true)));
            rows.Add(new List<object?>());

            Section("KEY METRICS");
            var kpiLabelRow = rows.Count;
            string Pc(string d) => DC(d) + " (" + N(Js.Round(DC(d) / (double)total * 100)) + "%)";
            var kpis = new (string Label, object Val, string? Z, string Font, string Fill)[]
            {
                ("Total Lines Assessed", (double)total, null, "333333", "EDEFF4"),
                ("Retain", Pc("Retain"), null, DecColors2["Retain"].Font, DecColors2["Retain"].Fill),
                ("Watch List", Pc("Watch List"), null, DecColors2["Watch List"].Font, DecColors2["Watch List"].Fill),
                ("Reduce Qty", Pc("Reduce Qty"), null, DecColors2["Reduce Qty"].Font, DecColors2["Reduce Qty"].Fill),
                ("Cancel", Pc("Cancel"), null, DecColors2["Cancel"].Font, DecColors2["Cancel"].Fill),
                ("Qty at Risk (units)", Js.Round(qtyAtRisk), "#,##0", "B45309", "FEF0DC"),
                ("Value at Risk (₹)", Js.Round(valueAtRisk), "₹#,##0", "B3261E", "FDE7E5")
            };
            rows.Add(kpis.Select(k => (object?)k.Label).ToList());
            rows.Add(kpis.Select(k => (object?)k.Val).ToList());
            for (var c = 0; c < kpis.Length; c++)
            {
                var k = kpis[c];
                Push(new[] { (kpiLabelRow, c) }, JsStyle.Make(F(k.Font, true, 8), k.Fill, A("center", "center", true), Thin));
                Push(new[] { (kpiLabelRow + 1, c) }, JsStyle.Make(F(k.Font, true, 15), k.Fill, A("center", "center"), Thin));
                if (k.Z != null) SetLastZ(k.Z);
            }
            rows.Add(new List<object?>());

            Section("DECISION MIX");
            var dmHdr = rows.Count;
            rows.Add(new List<object?> { "Decision", "Count", "% of Lines" });
            var hdrStyle = JsStyle.Make(F(White, true, 9), NavyDark, A("center"), Thin);
            Push(new[] { (dmHdr, 0), (dmHdr, 1), (dmHdr, 2) }, hdrStyle);
            var decShown = decOrder2.Where(d => DC(d) > 0).ToList();
            foreach (var d in decShown)
            {
                var pct = DC(d) / (double)total * 100;
                var rr = rows.Count;
                rows.Add(new List<object?> { d, (double)DC(d), N(Js.Round(pct * 10) / 10) + "%" });
                var dc = DecColors2.TryGetValue(d, out var x) ? x : ("333333", "FFFFFF");
                Push(new[] { (rr, 0), (rr, 1), (rr, 2) }, JsStyle.Make(F(dc.Item1, true, 9), dc.Item2, A("center"), Thin));
            }
            sh.Put(dmHdr, 4, "Category Head"); sh.Put(dmHdr, 5, "Watch List"); sh.Put(dmHdr, 6, "Reduce/Cancel"); sh.Put(dmHdr, 7, "Total");
            Push(new[] { (dmHdr, 4), (dmHdr, 5), (dmHdr, 6), (dmHdr, 7) }, hdrStyle);
            var workload = new JsObjectMap<(int WL, int RC)>();
            foreach (var name in allNames)
                foreach (var b in BucketOrder)
                {
                    if (!groups.TryGetValue(name + "|" + b.Code, out var g) || g.Rows.Count == 0) continue;
                    var w = workload.ContainsKey(name) ? workload[name] : (WL: 0, RC: 0);
                    if (b.Type == "WATCHLIST") w.WL += g.Rows.Count; else w.RC += g.Rows.Count;
                    workload[name] = w;
                }
            var wls = workload.ToList();
            JsSort.StableSort(wls, (a, b) => (b.Value.WL + b.Value.RC) - (a.Value.WL + a.Value.RC));
            for (var i = 0; i < wls.Count; i++)
            {
                var (name, w) = (wls[i].Key, wls[i].Value);
                var rr = dmHdr + 1 + i;
                sh.Put(rr, 4, name); sh.Put(rr, 5, (double)w.WL); sh.Put(rr, 6, (double)w.RC); sh.Put(rr, 7, (double)(w.WL + w.RC));
                var bg = i % 2 == 1 ? LtGrey : White;
                Push(new[] { (rr, 4) }, JsStyle.Make(F(colorFor(name), true, 9), bg, null, Thin));
                Push(new[] { (rr, 5) }, JsStyle.Make(F(DecColors2["Watch List"].Font, null, 9), bg, A("center"), Thin));
                Push(new[] { (rr, 6) }, JsStyle.Make(F(DecColors2["Cancel"].Font, null, 9), bg, A("center"), Thin));
                Push(new[] { (rr, 7) }, JsStyle.Make(F(null, true, 9), bg, A("center"), Thin));
            }
            var dmMax = Math.Max(dmHdr + decShown.Count, dmHdr + wls.Count);
            while (rows.Count <= dmMax) rows.Add(new List<object?>());
            if (rows.Count > dmMax + 1) rows.RemoveRange(dmMax + 1, rows.Count - dmMax - 1);
            rows.Add(new List<object?>());

            Section("RISK BY MERCHANDISE CATEGORY (MC)" + (mcAll.Count > 20 ? " — top 20 of " + mcAll.Count : ""));
            var riskHdr = rows.Count;
            rows.Add(new List<object?> { "MC Desc", "Lines", "Retain", "Watch List", "Reduce/Cancel", "Value at Risk (₹)", "Risk %" });
            Push(Enumerable.Range(0, 7).Select(c => (riskHdr, c)), JsStyle.Make(F(White, true, 9), NavyDark, A("center", null, true), Thin));
            for (var i = 0; i < mcTop.Count; i++)
            {
                var (mc, v) = (mcTop[i].Key, mcTop[i].Value);
                var riskPct = Js.Round((double)(v.Cut + v.Watch) / v.Total * 100);
                var rr = rows.Count;
                rows.Add(new List<object?> { mc, (double)v.Total, (double)v.Retain, (double)v.Watch, (double)v.Cut, Js.Round(v.Value), N(riskPct) + "%" });
                var bg = i % 2 == 1 ? LtGrey : White;
                Push(Enumerable.Range(0, 6).Select(c => (rr, c)), JsStyle.Make(F(null, null, 9), bg, A("center"), Thin));
                SetLastZ("₹#,##0;(₹#,##0);\"-\"");
                var rc = riskPct >= 50 ? ("B3261E", "FDE7E5") : riskPct >= 20 ? ("B45309", "FEF0DC") : ("1E7B45", "E5F4EA");
                Push(new[] { (rr, 6) }, JsStyle.Make(F(rc.Item1, true, 9), rc.Item2, A("center"), Thin));
            }
            rows.Add(new List<object?>());

            var qlRefs = new List<(int Idx, string Sheet, string Name, string Type)>();
            int? qlHdr = null, qlLast = null;
            if (CatHeadMap().Count == 0)
            {
                rows.Add(new List<object?> { "(No Cat Head Mapping uploaded — everything below is under \"Unassigned\")" });
                rows.Add(new List<object?>());
            }
            if (allNames.Count > 0)
            {
                Section("QUICK LINKS — click any ▼ below to filter by Category Head, Type, or Sub-Category");
                qlHdr = rows.Count;
                rows.Add(new List<object?> { "Category Head", "Type", "Sub-Category", "Description", "Item Count", "Go to Sheet" });
                Push(Enumerable.Range(0, 6).Select(c => (qlHdr.Value, c)), JsStyle.Make(F(White, true, 9), NavyDark, A("center"), Thin));
                var ql = 0;
                foreach (var name in allNames)
                    foreach (var b in BucketOrder)
                    {
                        if (!groups.TryGetValue(name + "|" + b.Code, out var g) || g.Rows.Count == 0) continue;
                        qlRefs.Add((rows.Count, sheetNames[name + "|" + b.Code], name, b.Type));
                        var bg = ql % 2 == 1 ? LtGrey : White;
                        var rr = rows.Count;
                        Push(new[] { (rr, 2), (rr, 3), (rr, 4), (rr, 5) }, JsStyle.Make(F(null, null, 9), bg, A("center", "center", true), Thin));
                        rows.Add(new List<object?> { name, b.Type, b.Sub, BucketDesc[b.Code], (double)g.Rows.Count, "→ Open" });
                        ql++;
                    }
                qlLast = rows.Count - 1;
                rows.Add(new List<object?>());
            }
            else { rows.Add(new List<object?> { "(No Watchlist / Reduce / Cancel items this run)" }); rows.Add(new List<object?>()); }
            Section("HOW THIS PORTAL REACHES ITS DECISIONS");
            rows.Add(new List<object?> { "This portal checks every raised PO against how well it is actually selling." });
            rows.Add(new List<object?> { "Single Concept articles are checked against their own stock-cover days (STR); Multi Concept articles are ranked against similar styles’ sell-through (PDSTR / quartile)." });
            rows.Add(new List<object?> { "Retain = healthy. Reduce Qty = Single Concept, high STR, still viable at a smaller qty. Cancel = confirmed dead stock, Single or Multi Concept. Watch List = borderline peer ranking, or a data pattern like a broken size curve — needs a human call, not an automatic one." });

            sh.Cols = new[] { 20, 16, 18, 46, 12, 14, 14, 12 }.Select(w => new Tool2ColumnDto { Width = w }).ToList();
            sh.Merges = new List<string> { "A1:H1" };
            foreach (var (r, c, s, z) in styles)
            {
                var ex = sh.Get(r, c) ?? new JsStyle();
                sh.Set(r, c, ex.Assign(s));
                if (z != null) sh.Z[(r, c)] = z;
            }
            foreach (var (idx, sheet, name, type) in qlRefs)
            {
                var target = "#'" + sheet + "'!A1";
                sh.L[(idx, 5)] = (target, "Go to " + sheet);
                sh.Set(idx, 0, JsStyle.Make(F(colorFor(name), true, null, null, true), null, null, Thin));
                sh.L[(idx, 0)] = (target, "Go to " + sheet);
                var tc = TypeColors.TryGetValue(type, out var t) ? t : TypeColors["WATCHLIST"];
                sh.Set(idx, 1, JsStyle.Make(F(tc.Font, true), tc.Fill, A("center"), Thin));
                sh.Set(idx, 5, JsStyle.Make(F(colorFor(name), true, null, null, true), null, A("center"), Thin));
            }
            if (qlHdr != null) sh.AutoFilter = Addr(qlHdr.Value, 0) + ":" + Addr(qlLast!.Value, 5);
            return sh;
        }

        private static readonly List<object?> BobsHeader = new() { "PO Number", "Generic Material", "Gen Mat Desc", "Vendor Name", "Colour", "Brand Type", "Division", "MC Description", "MRP (₹)", "Delivery Month", "Released Qty", "PO Value (₹)",
            "Quartile (Tool 1 Peer Rank)", "Avg per day sell thru% of that MC MRP", "Q1 (Top-Quartile) Peer Avg %/Day", "Decision Reason", "GenMaterial Relative Colours (with PDSTR & Quartile, desc.)" };
        private static readonly List<object?> TrHeader = new() { "PO Number", "Generic Material", "Gen Mat Desc", "Vendor Name", "Colour", "Brand Type", "Division", "MC Description", "MRP (₹)", "Delivery Month", "Released Qty", "PO Value (₹)",
            "Quartile (Tool 1 Peer Rank)", "Percentile", "PDSTR %/day", "Peer Avg %/day", "Q1 Peer Avg %/Day", "Peer Pool Size", "BSR Source", "Decision Reason", "Recommendation" };
        private static readonly List<object?> ScWatchHeader = new() { "PO Number", "Generic Material", "Gen Mat Desc", "Vendor Name", "Colour", "Brand Type", "Division", "MC Description", "MRP (₹)", "Delivery Month", "Released Qty", "PO Value (₹)",
            "Colour Mix %", "Mix % Threshold", "Decision Reason", "Recommendation" };
        private static readonly List<object?> RcMcHeader = new() { "PO Number", "Generic Material", "Gen Mat Desc", "Vendor Name", "Colour", "Brand Type", "Division", "MC Description", "MRP (₹)", "Delivery Month", "Released Qty", "Recommended Qty", "PO Value (₹)", "Value at Risk (₹)",
            "Engine", "Option Type", "STR (days)", "Quartile (Tool 1 Peer Rank)", "Percentile", "PDSTR %/day", "Peer Avg %/day", "Decision", "Decision Reason", "PO Status", "Recommendation",
            "Option 1 (Best)", "Option 2", "Option 3" };
        private static readonly List<object?> RcScHeader = new() { "PO Number", "Generic Material", "Gen Mat Desc", "Vendor Name", "Colour", "Brand Type", "Division", "MC Description", "MRP (₹)", "Delivery Month", "Released Qty", "Recommended Qty", "PO Value (₹)", "Value at Risk (₹)",
            "Engine", "STR (days)", "Decision", "Decision Reason", "PO Status", "Recommendation",
            "Option 1 (Best)", "Option 2", "Option 3" };

        private List<object?> Common(Tool2LineResult r) => new()
        {
            Or(r.PoNum, "—"), r.Gm, Or(r.Info?.ArtDesc, "—"), Or(r.Info?.VendorName, "—"), r.Colour, r.BrandOrNonPl, r.Div, r.McDesc, MrpNum(r), r.Dm, r.RelQty
        };

        private static object PctNum(double? v) => v != null ? Js.ParseFloat(Pct1(v.Value)) : "—";

        private List<object?> RowFor(string code, Tool2LineResult r)
        {
            var row = Common(r);
            switch (code)
            {
                case "WL-BoBs":
                    row.AddRange(new object?[] { PoValueCell(r), Or(r.Quartile, "—"), PctNum(r.PeerAvg), Q1PeerAvgPdstr(r), r.DecisionReason ?? "", AllSiblingsWithPdstr(r) });
                    return row;
                case "WL-TR":
                    row.AddRange(new object?[] { PoValueCell(r), Or(r.Quartile, "—"), r.Percentile != null ? r.Percentile.Value : "—", PctNum(r.Pdstr), PctNum(r.PeerAvg), Q1PeerAvgPdstr(r),
                        r.CleanPool != null ? (double)r.CleanPool.Count : "—", Or(r.BsrLabel, "—"), r.DecisionReason ?? "", "Watchlist (Category Head Review)" });
                    return row;
                case "WL-SC":
                    row.AddRange(new object?[] { PoValueCell(r), Js.ParseFloat(Pct1(r.MixPct)), P.Mix, r.DecisionReason ?? "",
                        FormatColourOptions(RankedColoursB(r, 3), "These colours in this MC currently have healthy sell-through: ") });
                    return row;
            }
            var cut = r.CutQty;
            var rate = r.PoRate ?? Js.Number(r.Mrp);
            var rowVal = double.IsFinite(rate) && rate > 0 ? Js.Round(rate * cut) : 0;
            var opts = ReduceCancelOptions(r, 3);
            string O(int i) => i < opts.Count ? opts[i] : "—";
            row.AddRange(new object?[] { r.RecQty, PoValueCell(r), rowVal, EngLabel(r) });
            if (code == "RC-MC")
                row.AddRange(new object?[] { Or(r.OptionType, "—"), r.ArtStr != null ? Js.Round(r.ArtStr.Value) : "—",
                    r.Engine == "A" ? Or(r.Quartile, "—") : "—", r.Engine == "A" && r.Percentile != null ? r.Percentile.Value : "—",
                    r.Engine == "A" ? PctNum(r.Pdstr) : "—", r.Engine == "A" ? PctNum(r.PeerAvg) : "—" });
            else
                row.Add(r.ArtStr != null ? Js.Round(r.ArtStr.Value) : "—");
            row.AddRange(new object?[] { r.Decision, r.DecisionReason ?? "", PoStatusFlag(r), ReduceCancelRecommendation(r, opts), O(0), O(1), O(2) });
            return row;
        }

        private static List<object?> HeaderFor(string code) => code switch
        {
            "WL-BoBs" => BobsHeader, "WL-TR" => TrHeader, "WL-SC" => ScWatchHeader, "RC-SC" => RcScHeader, _ => RcMcHeader
        };

        private Sheet GroupSheet(string sheetName, string name, Tool2BucketDto b, List<Tool2LineResult> rows)
        {
            var header = HeaderFor(b.Code);
            var data = rows.Select(r => RowFor(b.Code, r)).ToList();
            var sh = new Sheet { Name = sheetName };
            sh.Rows.Add(new List<object?> { "← Back to Summary" });
            sh.Rows.Add(new List<object?> { b.Type + " — " + b.Sub + " (" + data.Count + " item(s))" });
            sh.Rows.Add(header.ToList());
            sh.Rows.AddRange(data);

            var sections = new List<(int Title, int Header, int Last, int NCols)>();
            if (b.Code == "RC-SC")
            {
                var seen = new Dictionary<string, ScBucket>(StringComparer.Ordinal); var order = new List<string>();
                foreach (var bk in SingleConceptUniverseForCatHead(name))
                {
                    var key = Js.N(bk.Brand) + "|" + Js.N(bk.McCd) + "|" + Js.N(bk.Mrp) + "|" + Js.N(bk.Flag);
                    if (seen.TryAdd(key, bk)) order.Add(key);
                }
                foreach (var k in order)
                {
                    var bk = seen[k];
                    var table = BuildColourRecTable(bk.McCd, bk.Mrp, bk.Brand, bk.Flag);
                    if (table == null) continue;
                    sh.Rows.Add(new List<object?>());
                    var titleRow = sh.Rows.Count;
                    sh.Rows.Add(new List<object?> { "COLOUR-WISE STOCK & PO RECOMMENDATION — " + Or(bk.McDesc, "—") + " · MRP " + bk.Mrp + " · " + bk.Brand +
                                                    (bk.Flag.Length > 0 ? " · " + bk.Flag : "") + " · " + table.ColCount + " colours (Watch List excluded) · EffT=" + N(table.Eff) + "d" });
                    var headerRow = sh.Rows.Count;
                    sh.Rows.Add(table.Header);
                    sh.Rows.AddRange(table.DataRows);
                    sections.Add((titleRow, headerRow, sh.Rows.Count - 1, table.Header.Count));
                }
            }

            var wide = new HashSet<string> { "Decision Reason", "Recommendation", "GenMaterial Relative Colours (with PDSTR & Quartile, desc.)", "Gen Mat Desc", "MC Description" };
            var med = new HashSet<string> { "Vendor Name", "BSR Source", "PO Status", "Option 1 (Best)", "Option 2", "Option 3" };
            sh.Cols = header.Select(h => wide.Contains((string)h!) ? new Tool2ColumnDto { Width = 42, Hidden = false } : med.Contains((string)h!) ? new Tool2ColumnDto { Width = 20 } : new Tool2ColumnDto { Width = 13 }).ToList();

            var border = Thin;
            sh.Set(0, 0, JsStyle.Make(F(White, true, 10), Navy, A("left", "center")));
            sh.Merges = new List<string> { Addr(1, 0) + ":" + Addr(1, header.Count - 1) };
            sh.Set(1, 0, JsStyle.Make(F(White, true, 13), Navy, A("left", "center", null, 1)));
            for (var c = 0; c < header.Count; c++)
                if (sh.Exists(2, c)) sh.Set(2, c, JsStyle.Make(F(White, true, 10), NavyDark, A("center", "center", true), border));
            var currency = new HashSet<string> { "MRP (₹)", "PO Value (₹)", "Value at Risk (₹)" };
            var qty = new HashSet<string> { "Released Qty", "Recommended Qty" };
            for (var ri = 0; ri < data.Count; ri++)
            {
                var r = 3 + ri; var bg = ri % 2 == 1 ? LtGrey : White;
                for (var c = 0; c < header.Count; c++)
                {
                    if (!sh.Exists(r, c)) continue;
                    var ex = sh.Get(r, c) ?? new JsStyle();
                    var add = new JsStyle
                    {
                        Fill = ex.HasFill ? ex.Fill : bg, HasFill = true, Border = border, HasBorder = true,
                        Font = ex.HasFont ? ex.Font : F(null, null, 10), HasFont = true
                    };
                    sh.Set(r, c, ex.Assign(add));
                    var h = (string)header[c]!;
                    if (currency.Contains(h)) sh.Z[(r, c)] = "₹#,##0;(₹#,##0);\"-\"";
                    else if (qty.Contains(h)) sh.Z[(r, c)] = "#,##0;(#,##0);\"-\"";
                }
            }
            sh.L[(0, 0)] = ("#'" + SummarySheet + "'!A1", "Back to Summary");
            if (data.Count > 0) sh.AutoFilter = Addr(2, 0) + ":" + Addr(2 + data.Count, header.Count - 1);
            var decIdx = header.IndexOf("Decision");
            if (decIdx >= 0)
                for (var ri = 0; ri < data.Count; ri++)
                {
                    if (data[ri][decIdx] is not string d || !XlsDecisionColors.TryGetValue(d, out var dc)) continue;
                    if (!sh.Exists(3 + ri, decIdx)) continue;
                    var ex = sh.Get(3 + ri, decIdx) ?? new JsStyle();
                    sh.Set(3 + ri, decIdx, ex.Assign(JsStyle.Make(F(dc.Font, true, 10), dc.Fill, A("center", "center"))));
                }
            for (var i = 0; i < sections.Count; i++)
            {
                var sec = sections[i];
                var band = i % 2 == 0 ? "EAF0FB" : "FDF2F8";
                var n = sec.NCols > 0 ? sec.NCols : 7;
                sh.Merges.Add(Addr(sec.Title, 0) + ":" + Addr(sec.Title, n - 1));
                if (sh.Exists(sec.Title, 0)) sh.Set(sec.Title, 0, JsStyle.Make(F(White, true, 10), "B45309", A("left", "center", true, 1)));
                var numeric = new HashSet<int> { 2, 3 };
                for (var c = 7; c < n; c += 2) { numeric.Add(c); numeric.Add(c + 1); }
                for (var r = sec.Header; r <= sec.Last; r++)
                {
                    var isHeader = r == sec.Header; var isBest = r == sec.Header + 1;
                    for (var c = 0; c < n; c++)
                    {
                        if (!sh.Exists(r, c)) continue;
                        var ex = sh.Get(r, c) ?? new JsStyle();
                        var fill = isHeader ? "1A2B4C" : isBest ? "FFF3CD" : band;
                        var font = isHeader ? F(White, true, 9) : isBest ? F("333333", true, 9) : F("333333", null, 9);
                        if (!isHeader && sh.Cell(r, c) is string sv && sv == "Hold") font = F("888888", null, 9, true);
                        sh.Set(r, c, ex.Assign(JsStyle.Make(font, fill, A(isHeader ? "center" : (c <= 1 ? "left" : "right"), "center", isHeader), border)));
                        if (!isHeader && numeric.Contains(c) && sh.Cell(r, c) is double) sh.Z[(r, c)] = "#,##0;(#,##0);\"-\"";
                    }
                }
            }
            return sh;
        }

        private static Tool2SheetDto ToDto(Sheet s)
        {
            var dto = new Tool2SheetDto { Name = s.Name, Rows = s.Rows, Cols = s.Cols, Merges = s.Merges, AutoFilter = s.AutoFilter };
            var keys = s.S.Keys.Concat(s.Z.Keys).Concat(s.L.Keys).Distinct();
            foreach (var k in keys)
            {
                var st = new Tool2CellStyleDto();
                if (s.S.TryGetValue(k, out var js))
                {
                    if (js.Font is { } f) { st.FontColor = f.Color; st.Bold = f.Bold; st.Italic = f.Italic; st.Underline = f.Underline; st.Size = f.Sz; }
                    st.Fill = js.Fill;
                    if (js.Align is { } a) { st.HAlign = a.H; st.VAlign = a.V; st.Wrap = a.Wrap; st.Indent = a.Indent; }
                    st.Border = js.Border;
                }
                if (s.Z.TryGetValue(k, out var z)) st.NumFmt = z;
                if (s.L.TryGetValue(k, out var l)) { st.Link = l.Target; st.LinkTooltip = l.Tip; }
                dto.Styles[Addr(k.R, k.C)] = st;
            }
            return dto;
        }
    }
}

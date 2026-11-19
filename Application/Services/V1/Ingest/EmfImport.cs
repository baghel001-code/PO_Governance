using System.Text.RegularExpressions;
using Application.DTOs.Ingest;
using Application.Services.V1.Ingest.SheetJs;
using DecisionEngine.Common;
using DecisionEngine.Model;

namespace Application.Services.V1.Ingest
{
    /// <summary>
    /// Reads the Entry / Mid / Fashion mapping upload (dataset <see cref="DatasetKeys.Emf"/>): Brand × MC × Flag × MRP → tag.
    /// <list type="bullet">
    /// <item>The header row is found in the first 30 rows of any sheet (blank rows above it are fine): Brand (At_Brand / Brand
    /// Type), MC (MC Code / MC CD), Flag, MRP and the tag (EMF / Entry Mid Fashion / Tag); Division and the MC description
    /// (at_Desc / MC DESC) are kept when present.</item>
    /// <item>A tag must be Entry, Mid or Fashion (<see cref="EmfMapping.NormalizeTag"/>: "1.ENTRY", "2.MID", "3.FASHION" are
    /// fine). Any other tag, a blank tag, a missing Brand / MC / Flag / MRP, or the same Brand × MC × Flag × MRP tagged
    /// differently in the file → the row is <b>rejected</b> and listed (file, sheet, Excel row, key, tag, reason).</item>
    /// <item>An exact repeat (same key and tag) is loaded once and counted.</item>
    /// </list>
    /// The rows that pass replace the whole previous mapping (single dataset, not accumulated).
    /// </summary>
    public static class EmfImport
    {
        private const int HeaderScanRows = 30;

        private static readonly string[] BrandHeaders = { "atbrand", "brand", "brandtype", "brandname" };
        private static readonly string[] McHeaders = { "mccode", "mccd", "mc", "mcno" };
        private static readonly string[] FlagHeaders = { "flag", "flagbogononbogo", "bogononbogo", "articletype" };
        private static readonly string[] MrpHeaders = { "mrp", "mrpvalue" };
        private static readonly string[] TagHeaders = { "emf", "entrymidfashion", "emftag", "emftype", "tag", "type", "mrptype", "mcmrptype" };
        private static readonly string[] DivisionHeaders = { "division", "div" };
        private static readonly string[] McDescHeaders = { "atdesc", "mcdesc", "mcdes", "mcdescription" };

        private static readonly Regex NonAlnum = new("[^a-z0-9]", RegexOptions.CultureInvariant);

        /// <summary>The message when no sheet has the columns.</summary>
        public const string ColumnsMissing =
            "Columns not found: the file needs Brand (At_Brand), MC Code, Flag, MRP and EMF (Entry / Mid / Fashion) columns";

        /// <summary>What one or more mapping files produced.</summary>
        public sealed class Result
        {
            public List<SheetRow> Rows { get; } = new();
            public List<IngestRejectedRow> Rejected { get; } = new();
            public int DuplicatesIgnored { get; set; }
            /// <summary>Files where no sheet had the columns.</summary>
            public List<string> NoHeader { get; } = new();
            /// <summary>Data rows read (not blank), before the checks.</summary>
            public int RowsRead { get; set; }
        }

        private sealed record Candidate(IngestRejectedRow Where, string Key, string Tag, SheetRow Row);

        private static string Norm(string? h) => NonAlnum.Replace((h ?? "").Trim().ToLowerInvariant(), "");

        private static int Find(string[] normHeaders, string[] names)
        {
            foreach (var n in names)
            {
                var i = Array.IndexOf(normHeaders, n);
                if (i >= 0) return i;
            }
            return -1;
        }

        private static string StripDot0(string s) => s.EndsWith(".0", StringComparison.Ordinal) ? s[..^2] : s;

        /// <summary>Reads every file (name, workbook), then applies the checks over all of them together.</summary>
        public static Result Parse(IEnumerable<(string FileName, WorkbookData Workbook)> files)
        {
            var res = new Result();
            var candidates = new List<Candidate>();
            var order = new Dictionary<(string, string), int>();   // (file, sheet) → reading order, for the rejected list
            foreach (var (fileName, wb) in files)
            {
                var found = false;
                foreach (var sn in wb.SheetNames)
                {
                    if (!wb.Sheets.TryGetValue(sn, out var ws)) continue;
                    order.TryAdd((fileName, sn), order.Count);
                    var raw = SheetJson.ToRows(ws);
                    var startRow = ws.Range?.StartRow ?? 0;
                    for (var h = 0; h < Math.Min(raw.Count, HeaderScanRows); h++)
                    {
                        var norm = raw[h].Select(Norm).ToArray();
                        int cBrand = Find(norm, BrandHeaders), cMc = Find(norm, McHeaders), cFlag = Find(norm, FlagHeaders),
                            cMrp = Find(norm, MrpHeaders), cTag = Find(norm, TagHeaders);
                        if (cBrand < 0 || cMc < 0 || cFlag < 0 || cMrp < 0 || cTag < 0) continue;
                        int cDiv = Find(norm, DivisionHeaders), cDesc = Find(norm, McDescHeaders);
                        found = true;
                        for (var i = h + 1; i < raw.Count; i++)
                        {
                            var row = raw[i];
                            if (row.All(v => string.IsNullOrWhiteSpace(v))) continue;
                            res.RowsRead++;
                            string V(int c) => c >= 0 && c < row.Length ? JsNum.Trim(row[c] ?? "") : "";
                            var where = new IngestRejectedRow
                            {
                                File = fileName, Sheet = sn, Row = startRow + i + 1,
                                Brand = V(cBrand), Mc = StripDot0(V(cMc)), Flag = V(cFlag), Mrp = StripDot0(V(cMrp)), Tag = V(cTag)
                            };
                            var reasons = new List<string>();
                            var tag = EmfMapping.NormalizeTag(where.Tag);
                            if (tag == null)
                                reasons.Add(where.Tag.Length == 0 ? "No tag — must be Entry, Mid or Fashion" : "Tag \"" + where.Tag + "\" is not Entry, Mid or Fashion");
                            var missing = new[] { ("Brand", where.Brand), ("MC", where.Mc), ("Flag", where.Flag), ("MRP", where.Mrp) }
                                .Where(x => x.Item2.Length == 0).Select(x => x.Item1).ToList();
                            if (missing.Count > 0) reasons.Add(string.Join(", ", missing) + " missing");
                            if (reasons.Count > 0)
                            {
                                where.Reason = string.Join("; ", reasons);
                                res.Rejected.Add(where);
                                continue;
                            }
                            var stored = new SheetRow(new KeyValuePair<string, object?>[]
                            {
                                new(EmfMapping.ColBrand, where.Brand), new(EmfMapping.ColDivision, V(cDiv)), new(EmfMapping.ColMc, where.Mc),
                                new(EmfMapping.ColMcDesc, V(cDesc)), new(EmfMapping.ColFlag, where.Flag), new(EmfMapping.ColMrp, where.Mrp),
                                new(EmfMapping.ColTag, tag!)
                            });
                            candidates.Add(new Candidate(where, EmfMapping.Key(where.Brand, where.Mc, where.Flag, where.Mrp), tag!, stored));
                        }
                        break;   // one header per sheet
                    }
                }
                if (!found) res.NoHeader.Add(fileName);
            }

            // the same Brand × MC × Flag × MRP twice: same tag → loaded once; different tags → every such row rejected
            var conflicts = candidates.GroupBy(c => c.Key).Where(g => g.Select(c => c.Tag).Distinct().Count() > 1)
                                      .ToDictionary(g => g.Key, g => string.Join(" and ", g.Select(c => c.Tag).Distinct()));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in candidates)
            {
                if (conflicts.TryGetValue(c.Key, out var tags))
                {
                    c.Where.Reason = "Same Brand, MC, Flag and MRP is tagged " + tags + " in the file";
                    res.Rejected.Add(c.Where);
                    continue;
                }
                if (!seen.Add(c.Key)) { res.DuplicatesIgnored++; continue; }
                res.Rows.Add(c.Row);
            }
            var sorted = res.Rejected.OrderBy(r => order.GetValueOrDefault((r.File, r.Sheet))).ThenBy(r => r.Row).ToList();
            res.Rejected.Clear();
            res.Rejected.AddRange(sorted);
            return res;
        }
    }
}

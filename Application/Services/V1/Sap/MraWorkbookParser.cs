using System.Globalization;
using System.Text.RegularExpressions;
using Application.DTOs.Ingest;
using DecisionEngine.Mra;

namespace Application.Services.V1.Sap
{
    /// <summary>
    /// The morning MRA workbook (v1.1.7 app/mra.js parseWorkbook + server.py api_POST_mra upload): one workbook, two
    /// sheets found by their headers (sheet names do not matter), within the first 15 rows of a sheet:
    ///   item sheet   DOC.NO, ARTICLE CODE, Color, Generic Article, TOTAL PO QTY, LEVEL (+ DEL. DATE required; CREATED BY,
    ///                Generic Desc. read) — one row per MRA line / site; TOTAL PO QTY added up per MRA;
    ///   header sheet Document Number, Purchase order, Status (+ Po Date, Created By (name / ID), Inspiration, Created on,
    ///                level) — the PO(s) of each MRA.
    /// Optional decision columns on either sheet: Tool Decision / Decision / Verdict, Decision Reason, Decision ID,
    /// Max Permissible (shown only, never used to count). Brand, MC, MRP, Flag always come from the Article Master.
    /// </summary>
    public static class MraWorkbookParser
    {
        private const int HeaderSearchRows = 15;
        private static readonly string[] ItemNeed = { "docno", "genericarticle", "color", "totalpoqty", "level" };
        private static readonly string[] HeadNeed = { "documentnumber", "purchaseorder", "status" };
        private static readonly (string Key, string[] Headers)[] DecisionCols =
        {
            ("decision", new[] { "tooldecision", "decision", "verdict", "toolverdict" }),
            ("decision_reason", new[] { "decisionreason", "verdictreason" }),
            ("decision_id", new[] { "decisionid", "verdictid", "decisionno" }),
            ("max_permissible", new[] { "maxpermissible", "maxpermissibleqty" }),
        };

        public sealed class Result
        {
            public string? Error { get; set; }
            public Dictionary<string, MraFileRecord> Mras { get; } = new(StringComparer.Ordinal);
            public int RowsItem { get; set; }
            public int RowsHeader { get; set; }
            public int Skipped { get; set; }
            public int Mixed { get; set; }
            public int HeaderOnly { get; set; }
            public int ItemOnly { get; set; }
            public int LevelDiff { get; set; }
            /// <summary>MRAs dropped by the normalisation (level / article / colour / date / qty not readable, or an unsafe number).</summary>
            public int Invalid { get; set; }
            public string? ItemSheet { get; set; }
            public string? HeaderSheet { get; set; }
        }

        public static string Hnorm(object? h) => Regex.Replace(Str(h).ToLowerInvariant(), "[^a-z0-9]", "");

        /// <summary>A cell as JS String(v) of a raw sheet_to_json value: numbers without trailing ".0", "" when empty.</summary>
        private static string Str(object? v) => v switch
        {
            null => "",
            double d => d % 1 == 0 && Math.Abs(d) < 1e15 ? d.ToString("0", CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => v.ToString() ?? ""
        };

        private static bool Blank(object? v) => v == null || (v is string s && s.Length == 0);

        /// <summary>Rows of a sheet as raw values (sheet_to_json header:1, raw:true, defval:'') from the range start.</summary>
        private static List<object?[]> Rows(SheetData ws)
        {
            var list = new List<object?[]>();
            if (ws.Range is not { } r) return list;
            for (var row = r.StartRow; row <= r.EndRow; row++)
            {
                var a = new object?[r.EndCol - r.StartCol + 1];
                for (var c = r.StartCol; c <= r.EndCol; c++)
                {
                    var cell = ws.Get(row, c);
                    a[c - r.StartCol] = cell?.Value ?? "";
                }
                list.Add(a);
            }
            return list;
        }

        private static int FindHeader(List<object?[]> rows, string[] need)
        {
            for (var i = 0; i < Math.Min(rows.Count, HeaderSearchRows); i++)
            {
                var hs = rows[i].Select(Hnorm).ToList();
                if (need.All(hs.Contains)) return i;
            }
            return -1;
        }

        /// <summary>mra.js toIso: Excel serial (20000–80000), yyyy-mm-dd…, dd.mm.yyyy / dd/mm/yyyy / dd-mm-yyyy → yyyy-MM-dd; else "".</summary>
        public static string ToIso(object? v)
        {
            if (Blank(v)) return "";
            if (v is double n && n > 20000 && n < 80000)
                return DateTime.FromOADate(Math.Round(n)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var s = Str(v).Trim();
            var m = Regex.Match(s, @"^(\d{4})-(\d{1,2})-(\d{1,2})");
            if (m.Success) return m.Groups[1].Value + "-" + m.Groups[2].Value.PadLeft(2, '0') + "-" + m.Groups[3].Value.PadLeft(2, '0');
            m = Regex.Match(s, @"^(\d{1,2})[./-](\d{1,2})[./-](\d{4})");
            if (m.Success) return m.Groups[3].Value + "-" + m.Groups[2].Value.PadLeft(2, '0') + "-" + m.Groups[1].Value.PadLeft(2, '0');
            return "";
        }

        private static Dictionary<string, int> DCols(List<string> hs)
        {
            var o = new Dictionary<string, int>();
            foreach (var (k, heads) in DecisionCols)
                foreach (var h in heads)
                {
                    var j = hs.IndexOf(h);
                    if (j >= 0) { o[k] = j; break; }
                }
            return o;
        }

        private static object? At(object?[] row, int i) => i >= 0 && i < row.Length ? row[i] : "";

        private sealed class Raw
        {
            public string Ga = "", Colour = "", Dd = "", Level = "", GaDesc = "", CreatedById = "", CreatedBy = "", Inspiration = "", CreatedOn = "";
            public double Qty;
            public int Lines;
            public List<(string Po, string Status, string PoDate)> Pos = new();
            public Dictionary<string, string> Dec = new();
        }

        public static Result Parse(WorkbookData wb)
        {
            var res = new Result();
            (string Name, List<object?[]> Rows, int H)? item = null, head = null;
            foreach (var n in wb.SheetNames)
            {
                var rows = Rows(wb.Sheets[n]);
                if (item == null)
                {
                    var hi = FindHeader(rows, ItemNeed);
                    if (hi >= 0 && FindHeader(rows, new[] { "articlecode" }) == hi) item = (n, rows, hi);
                }
                if (head == null)
                {
                    var hh = FindHeader(rows, HeadNeed);
                    if (hh >= 0) head = (n, rows, hh);
                }
            }
            if (item == null) { res.Error = "The item-lines sheet was not found (needs columns DOC.NO, ARTICLE CODE, Color, Generic Article, TOTAL PO QTY, LEVEL). Nothing was changed."; return res; }
            if (head == null) { res.Error = "The header sheet was not found (needs columns Document Number, Purchase order, Status). Nothing was changed."; return res; }
            res.ItemSheet = item.Value.Name; res.HeaderSheet = head.Value.Name;

            var H = item.Value.Rows[item.Value.H].Select(Hnorm).ToList();
            int Ix(string n) => H.IndexOf(n);
            var dI = DCols(H);
            int iDoc = Ix("docno"), iGa = Ix("genericarticle"), iCol = Ix("color"), iQty = Ix("totalpoqty"), iDd = Ix("deldate"), iLv = Ix("level"),
                iDesc = Ix("genericdesc"), iBy = Ix("createdby");
            if (iDd < 0) { res.Error = "The item-lines sheet has no DEL. DATE column. Nothing was changed."; return res; }

            var raw = new Dictionary<string, Raw>(StringComparer.Ordinal);
            var order = new List<string>();
            for (var r = item.Value.H + 1; r < item.Value.Rows.Count; r++)
            {
                var row = item.Value.Rows[r];
                if (row.All(Blank)) continue;
                res.RowsItem++;
                var no = Str(At(row, iDoc)).Trim().TrimStart('0');
                var g = MraNorm.Ga(Str(At(row, iGa)));
                var cc = Str(At(row, iCol)).Trim().ToUpperInvariant();
                var dd = ToIso(At(row, iDd));
                var lv = Str(At(row, iLv)).Trim();
                var qv = At(row, iQty);
                var qOk = double.TryParse(Str(qv).Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var q) || Blank(qv);
                if (Blank(qv)) q = 0;
                if (no.Length == 0 || g.Length == 0 || cc.Length == 0 || dd.Length == 0 || lv.Length == 0 || !qOk || double.IsNaN(q) || double.IsInfinity(q))
                {
                    res.Skipped++;
                    continue;
                }
                if (!raw.TryGetValue(no, out var m))
                {
                    raw[no] = m = new Raw { Ga = g, Colour = cc, Dd = dd, Level = lv, GaDesc = iDesc >= 0 ? Str(At(row, iDesc)) : "", CreatedById = iBy >= 0 ? Str(At(row, iBy)) : "" };
                    order.Add(no);
                    foreach (var (k, j) in dI) if (!Blank(At(row, j))) m.Dec[k] = Str(At(row, j)).Trim();
                }
                else if (m.Ga != g || m.Colour != cc || m.Dd != dd || m.Level != lv) res.Mixed++;
                m.Qty += q;
                m.Lines++;
            }

            var HH = head.Value.Rows[head.Value.H].Select(Hnorm).ToList();
            int Jx(string n, int from = 0) { for (var i = from; i < HH.Count; i++) if (HH[i] == n) return i; return -1; }
            int jDoc = Jx("documentnumber"), jPo = Jx("purchaseorder"), jPd = Jx("podate"), jSt = Jx("status"), jBy = Jx("createdby"),
                jBy2 = jBy >= 0 ? Jx("createdby", jBy + 1) : -1, jInsp = Jx("inspiration"), jOn = Jx("createdon"), jLv = Jx("level");
            var dJ = DCols(HH);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var k = head.Value.H + 1; k < head.Value.Rows.Count; k++)
            {
                var hr = head.Value.Rows[k];
                if (hr.All(Blank)) continue;
                res.RowsHeader++;
                var dn = Str(At(hr, jDoc)).Trim().TrimStart('0');
                if (dn.Length == 0) continue;
                seen.Add(dn);
                if (!raw.TryGetValue(dn, out var mm)) { res.HeaderOnly++; continue; }
                var po = jPo >= 0 ? Str(At(hr, jPo)).Trim() : "";
                if (po.Length > 0) mm.Pos.Add((po, jSt >= 0 ? Str(At(hr, jSt)) : "", jPd >= 0 ? ToIso(At(hr, jPd)) : ""));
                if (jBy >= 0 && !Blank(At(hr, jBy))) mm.CreatedBy = Str(At(hr, jBy));
                if (jBy2 >= 0 && !Blank(At(hr, jBy2)) && mm.CreatedById.Length == 0) mm.CreatedById = Str(At(hr, jBy2));
                if (jInsp >= 0) mm.Inspiration = Str(At(hr, jInsp));
                if (jOn >= 0) mm.CreatedOn = ToIso(At(hr, jOn));
                if (jLv >= 0 && Str(At(hr, jLv)).Trim().Length > 0 && Str(At(hr, jLv)).Trim() != mm.Level) res.LevelDiff++;
                foreach (var (kk, j) in dJ) if (!Blank(At(hr, j)) && !mm.Dec.ContainsKey(kk)) mm.Dec[kk] = Str(At(hr, j)).Trim();
            }
            res.ItemOnly = raw.Keys.Count(n => !seen.Contains(n));

            // server.py: the same normalisation as a live message; an MRA that does not pass is left out
            foreach (var no in order)
            {
                var r = raw[no];
                var lv = MraNorm.Level(r.Level);
                var ga = MraNorm.Ga(r.Ga);
                var col = MraNorm.Colour(r.Colour);
                var dd = MraNorm.Date(r.Dd);
                var qty = MraNorm.Qty(r.Qty.ToString("R", CultureInfo.InvariantCulture));
                var key = MraNorm.NoZeros(no);
                if (lv == null || ga.Length == 0 || col.Length == 0 || dd.Length == 0 || qty == null || !MraNorm.ValidNo(key)) { res.Invalid++; continue; }
                var rec = new MraFileRecord
                {
                    Ga = ga, Colour = col, Dd = dd, Qty = qty.Value, Level = lv, Lines = r.Lines,
                    CreatedBy = Cut(r.CreatedBy, 80), CreatedById = Cut(r.CreatedById, 40), CreatedOn = MraNorm.Date(r.CreatedOn) is { Length: > 0 } co ? co : null,
                    Inspiration = Cut(r.Inspiration, 60), GaDesc = Cut(r.GaDesc, 80)
                };
                foreach (var (po, st, pd) in r.Pos.Take(20))
                    if (MraNorm.ValidNo(po))
                        rec.Pos.Add(new MraFilePo { Po = MraNorm.NoZeros(po), Status = MraNorm.PoStatus(st), PoDate = MraNorm.Date(pd) is { Length: > 0 } x ? x : null });
                var dec = MraDecisionInfo.FromPick(names =>
                {
                    foreach (var n in names) if (r.Dec.TryGetValue(n, out var v) && v.Length > 0) return System.Text.Json.Nodes.JsonValue.Create(v);
                    return null;
                });
                if (dec.Any) rec.DecisionInfo = dec;
                res.Mras[key] = rec;
            }
            return res;
        }

        private static string? Cut(string s, int n) { s = (s ?? "").Trim(); return s.Length == 0 ? null : s.Length > n ? s[..n] : s; }
    }
}

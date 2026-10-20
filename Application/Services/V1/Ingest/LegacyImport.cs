using System.Text.RegularExpressions;
using Application.DTOs.Ingest;
using Application.Services.V1.Ingest.SheetJs;
using DecisionEngine.Model;

namespace Application.Services.V1.Ingest
{
    /// <summary>
    /// Faithful port of the Data Setup parsing functions of <c>legacy/python-app/app/index.html</c>
    /// (v1.1.4). Pure: works on <see cref="WorkbookData"/> read by the workbook reader; every sheet is
    /// first turned into <c>sheet_to_json(ws, {header:1, defval:'', raw:false})</c> rows
    /// (<see cref="SheetJson.ToRows"/>), so all values are display strings exactly as in the browser.
    /// </summary>
    public static class LegacyImport
    {
        /// <summary>index.html MULTI_KEYS (line 1651): keys whose upload reads every sheet and accumulates.</summary>
        public static readonly string[] MultiKeys = { "bsr" };
        /// <summary>index.html REQUIRED_KEYS (line 1652).</summary>
        public static readonly string[] RequiredKeys = { "f1", "f2", "f3", "f4", "um", "bsr" };

        /// <summary>Slot keys with a loadFile upload slot on the Data Setup page.</summary>
        public static readonly string[] SlotKeys = { "f1", "f2", "f3", "f4", "um", "bsr", "artmaster", "attrrel", "colmaster", "catheadmap" };

        /// <summary>REQUIRED_LABELS of loadZipBundle.</summary>
        public static readonly IReadOnlyDictionary<string, string> RequiredLabels = new Dictionary<string, string>
        {
            ["f1"] = "F1 — Stock", ["f2"] = "F2 — Pending PO", ["f3"] = "F3 — Sales Trend",
            ["f4"] = "F4 — Forecast", ["um"] = "Universe Master", ["bsr"] = "BSR (all seasons)"
        };

        /// <summary>rawKeyLabel / byKeyLabel of the zip and raw-import reports.</summary>
        public static readonly IReadOnlyDictionary<string, string> RawKeyLabels = new Dictionary<string, string>
        {
            ["f1"] = "F1 — Stock", ["f2"] = "F2 — Pending PO", ["f3"] = "F3 — Sales Trend",
            ["artmaster"] = "Article Master", ["bsr"] = "BSR — All Seasons"
        };

        public static bool IsMultiKey(string key) => Array.IndexOf(MultiKeys, key) >= 0;

        // ── shared predicates ───────────────────────────────────────────

        /// <summary><c>v && typeof v==='string' && v.trim().length>1 && isNaN(v)</c> — a header-like text cell.</summary>
        private static bool IsHeaderText(string? v) => !string.IsNullOrEmpty(v) && JsNum.Trim(v).Length > 1 && JsNum.IsNaN(v);

        private static int CountHeaderText(string?[] row)
        {
            var n = 0;
            foreach (var v in row) if (IsHeaderText(v)) n++;
            return n;
        }

        /// <summary>
        /// Port of _bestSheetName (index.html line 2000): the sheet whose first 10 rows hold the most
        /// header-like text cells (a decoy "Sheet1" scratch tab loses to the real data sheet).
        /// </summary>
        public static string? BestSheetName(WorkbookData wb)
        {
            var best = wb.SheetNames.Count > 0 ? wb.SheetNames[0] : null;
            var bestScore = -1;
            foreach (var sn in wb.SheetNames)
            {
                if (!wb.Sheets.TryGetValue(sn, out var ws)) continue;
                var raw = SheetJson.ToRows(ws);
                var score = 0;
                for (var i = 0; i < Math.Min(raw.Count, 10); i++)
                {
                    var n = CountHeaderText(raw[i]);
                    if (n > score) score = n;
                    if (n >= 3) break;
                }
                if (score > bestScore) { bestScore = score; best = sn; }
            }
            return best;
        }

        /// <summary>
        /// Port of loadFile → readOne (index.html line 2023) and the identical _parseWorkbookForKey
        /// (line 2144): header row = first of the first 10 rows with ≥3 header-like text cells (else row 0);
        /// each later non-blank row becomes {trimmed header: cell} (blank headers skipped, a repeated
        /// header keeps its first position and the later value). MULTI_KEYS read every sheet.
        /// </summary>
        public static List<SheetRow> ParseWorkbookForKey(WorkbookData wb, string key)
        {
            var rows = new List<SheetRow>();
            IEnumerable<string> sheetsToRead = IsMultiKey(key)
                ? wb.SheetNames
                : BestSheetName(wb) is { } b ? new[] { b } : Array.Empty<string>();
            foreach (var sn in sheetsToRead)
            {
                if (!wb.Sheets.TryGetValue(sn, out var ws)) continue;
                var raw = SheetJson.ToRows(ws);
                var hdrIdx = 0;
                for (var i = 0; i < Math.Min(raw.Count, 10); i++)
                {
                    if (CountHeaderText(raw[i]) >= 3) { hdrIdx = i; break; }
                }
                if (raw.Count <= hdrIdx + 1) continue;
                var headers = raw[hdrIdx];
                for (var i = hdrIdx + 1; i < raw.Count; i++)
                {
                    var row = raw[i];
                    if (row.All(v => v == null || v.Length == 0)) continue;
                    var obj = new JsObject();
                    for (var j = 0; j < headers.Length; j++)
                    {
                        var h = headers[j];
                        if (string.IsNullOrEmpty(h)) continue;
                        obj.Set(JsNum.Trim(h), (j < row.Length ? row[j] : null) ?? "");
                    }
                    rows.Add(obj.ToSheetRow());
                }
            }
            return rows;
        }

        // ── zip routing ─────────────────────────────────────────────────

        public sealed record ZipRule(string Key, string Label, Regex Pattern);

        private const string Ws = "[_" + JsNum.WsClass + "-]";
        private static Regex Rx(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Port of _zipRules (index.html line 2123): file-name patterns, first match wins.</summary>
        public static readonly IReadOnlyList<ZipRule> ZipRules = new List<ZipRule>
        {
            new("f1", "F1 — Stock", Rx("^F1" + Ws + "?STOCK")),
            new("f2", "F2 — Pending PO", Rx("^F2" + Ws + "?PENDING")),
            new("f3", "F3 — Sales Trend", Rx("^F3" + Ws + "?SALES")),
            new("f4", "F4 — Forecast", Rx("^F4" + Ws + "?FORECAST")),
            new("um", "Universe Master", Rx("UNIVERSE" + Ws + "?MASTER")),
            new("artmaster", "Article Master", Rx("ARTICLE" + Ws + "?MASTER")),
            new("colmaster", "Colour Master", Rx("COLOU?R" + Ws + "?MASTER")),
            new("attrrel", "Attribute Relevance Master", Rx("ATT(RIBUTE)?" + Ws + "?RELEVANCE")),
            new("catheadmap", "Cat Head Mapping", Rx("CAT(EGORY)?" + Ws + "?HEAD|SCR_PRODUCT")),
            new("bsr", "BSR — All Seasons", Rx("(BSR|BEST" + Ws + "?SELLER)")),
        };

        // ── raw system-export import ───────────────────────────────────

        private static readonly Regex NewLines = new("[\n\r]+", RegexOptions.CultureInvariant);
        private static readonly Regex Spaces = new("[" + JsNum.WsClass + "]+", RegexOptions.CultureInvariant);

        /// <summary>_rawHdr: newlines → space, collapse white space, trim, lower-case.</summary>
        public static string RawHdr(string? v)
        {
            var s = v ?? "";
            s = NewLines.Replace(s, " ");
            s = Spaces.Replace(s, " ");
            return JsNum.Trim(s).ToLowerInvariant();
        }

        /// <summary>_rawPick: value of the first target header present with a non-empty value.</summary>
        public static object? RawPick(JsObject rowObj, params string[] targets)
        {
            var keys = rowObj.Keys.ToList();
            foreach (var t in targets)
            {
                var nt = RawHdr(t);
                var k = keys.FirstOrDefault(x => RawHdr(x) == nt);
                if (k != null && !(rowObj[k] is string s && s.Length == 0)) return rowObj[k];
            }
            return "";
        }

        /// <summary>_rawPickAfter: the cell right of the labelled column (blank-header MC / article description).</summary>
        public static object? RawPickAfter(string?[] headerArr, string?[] dataArr, string targetHeader)
        {
            var nt = RawHdr(targetHeader);
            var idx = Array.FindIndex(headerArr, h => RawHdr(h) == nt);
            if (idx < 0 || idx + 1 >= dataArr.Length) return "";
            return dataArr[idx + 1] ?? "";
        }

        public sealed record RawClass(string Type, int HeaderRowIdx, int DateRowIdx = -1);

        /// <summary>_classifyRawSheet (index.html line 2336).</summary>
        public static RawClass? ClassifyRawSheet(List<string?[]> rows)
        {
            for (var i = 0; i < Math.Min(rows.Count, 12); i++)
            {
                var norm = rows[i].Select(RawHdr).ToList();
                if (norm.Any(h => h == "calendar day")) return new RawClass("sales_trend", i + 1, i);
                if (norm.Any(h => h.Contains("gen art")) && norm.Any(h => h.Contains("sum of pending qty"))) return new RawClass("po_pending", i);
                if (norm.Any(h => h.Contains("stk q") && h.Contains("in q"))) return new RawClass("stock", i);
                if (norm.Any(h => h == "week on floor") && norm.Any(h => h == "generic material") && norm.Any(h => h.Contains("avg perday sell thru")))
                    return new RawClass("bsr_detail", i);
                if (norm.Any(h => h == "generic material") && norm.Any(h => h == "merchandise category"))
                {
                    var attrHits = new[]
                    {
                        norm.Any(h => h == "style"),
                        norm.Any(h => h.Contains("cut") && h.Contains("fit")),
                        norm.Any(h => h == "design"),
                        norm.Any(h => h.Contains("base material") || h.Contains("product material"))
                    }.Count(x => x);
                    if (attrHits >= 2) return new RawClass("article_master", i);
                }
            }
            return null;
        }

        /// <summary>_rawObjFromHeaders.</summary>
        public static JsObject RawObjFromHeaders(string?[] headerArr, string?[] dataArr)
        {
            var o = new JsObject();
            for (var i = 0; i < headerArr.Length; i++)
            {
                var h = headerArr[i];
                if (string.IsNullOrEmpty(h)) continue;
                o.Set(JsNum.Trim(h), (i < dataArr.Length ? dataArr[i] : null) ?? "");
            }
            return o;
        }

        private static string Str(object? v) => v switch { null => "", string s => s, double d => JsNum.ToStr(d), _ => v.ToString() ?? "" };

        /// <summary>_transformPOPending → F2 row.</summary>
        public static JsObject TransformPOPending(string?[] headerArr, string?[] dataArr)
        {
            var o = RawObjFromHeaders(headerArr, dataArr);
            var r = new JsObject();
            r.Set("PO Number", RawPick(o, "PO"));
            r.Set("DIV", RawPick(o, "Division"));
            r.Set("MC CD", RawPick(o, "MC CD"));
            r.Set("MC DESC", RawPick(o, "MC DESC"));
            r.Set("MRP", RawPick(o, "MRP"));
            r.Set("Generic Material", RawPick(o, "GEN ART"));
            r.Set("Brand Type", RawPick(o, "Brand Type"));
            r.Set("BOGO/NON-BOGO", RawPick(o, "BOGO/NON-BOGO"));
            r.Set("Color", RawPick(o, "Color", "Colour"));
            r.Set("Delivery Month", RawPick(o, "Delivery Month"));
            r.Set("Vendor Name", RawPick(o, "Vendor Name"));
            r.Set("Pending Qty", RawPick(o, "Sum of Pending Qty (Each)", "Sum of Pending Qty"));
            r.Set("PO Value", RawPick(o, "Sum of Pending Value"));
            return r;
        }

        /// <summary>_transformStock → F1 row (OP STK = "Stk q + In Q").</summary>
        public static JsObject TransformStock(string?[] headerArr, string?[] dataArr)
        {
            var o = RawObjFromHeaders(headerArr, dataArr);
            var r = new JsObject();
            r.Set("DIV", RawPick(o, "Division"));
            r.Set("Brand Type", RawPick(o, "Brand Type"));
            r.Set("MC CD", RawPick(o, "Merchandise Category"));
            r.Set("MC DESC", RawPickAfter(headerArr, dataArr, "Merchandise Category"));
            r.Set("MRP", RawPick(o, "MRP"));
            r.Set("BOGO/NON-BOGO", RawPick(o, "Planning Flag"));
            r.Set("Generic Material", RawPick(o, "Generic Material"));
            r.Set("Color", RawPick(o, "Color", "Colour"));
            r.Set("OP STK", RawPick(o, "Stk q + In Q"));
            return r;
        }

        private static readonly Regex DateSplit = new(@"[.\/-]", RegexOptions.CultureInvariant);

        /// <summary><c>new Date(yy, mm-1, dd).getTime()</c> (local wall clock; NaN when invalid) — only compared.</summary>
        public static double JsDateValue(double yy, double mm0, double dd)
        {
            if (!double.IsFinite(yy) || !double.IsFinite(mm0) || !double.IsFinite(dd)) return double.NaN;
            var y = Math.Truncate(yy);
            if (y >= 0 && y <= 99) y += 1900;
            var m = Math.Truncate(mm0);
            var d = Math.Truncate(dd);
            var ym = y + Math.Floor(m / 12);
            if (Math.Abs(ym) > 400000) return double.NaN;
            var mn = (int)(((m % 12) + 12) % 12);
            var day = JsDate.DaysFromCivil((long)ym, mn + 1, 1) + d - 1;
            var t = day * 86400000.0 - JsDate.LocalOffsetMinutes * 60000.0;
            return Math.Abs(t) > 8.64e15 ? double.NaN : t;
        }

        /// <summary>_transformSalesTrend → F3 row with "Day 1".."Day N" (oldest first).</summary>
        public static JsObject TransformSalesTrend(string?[] headerArr, string?[] dateRowArr, string?[] dataArr)
        {
            var o = RawObjFromHeaders(headerArr, dataArr);
            var brand = RawPick(o, "Brand Type");
            var mcCd = RawPick(o, "Merchandise Category");
            var mrp = RawPick(o, "MRP");
            var flag = RawPick(o, "Planning Flag");
            var colour = RawPick(o, "Color", "Colour");
            var r = new JsObject();
            r.Set("DIV", RawPick(o, "Division"));
            r.Set("Brand Type", brand);
            r.Set("MC CD", mcCd);
            r.Set("MC DESC", RawPickAfter(headerArr, dataArr, "Merchandise Category"));
            r.Set("MRP", mrp);
            r.Set("BOGO/NON-BOGO", flag);
            r.Set("Generic Material", RawPick(o, "Generic Material"));
            r.Set("Color", colour);
            r.Set("KEY-1", Str(brand) + "-" + Str(mcCd));
            r.Set("KEY-2", Str(brand) + "-" + Str(mcCd) + "-" + Str(mrp) + "-" + Str(flag));
            r.Set("KEY-3", Str(brand) + "-" + Str(mcCd) + "-" + Str(mrp) + "-" + Str(flag) + "-" + Str(colour));
            var dayCols = new List<(int Idx, double Ts, object? Raw)>();
            for (var i = 0; i < headerArr.Length; i++)
            {
                var dr = i < dateRowArr.Length ? dateRowArr[i] : null;
                if (!RawHdr(headerArr[i]).Contains("sales qty") || string.IsNullOrEmpty(dr)) continue;
                var d = JsNum.Trim(dr);
                var parts = DateSplit.Split(d);
                var ts = double.NaN;
                if (parts.Length == 3)
                {
                    double dd = JsNum.ToNumber(parts[0]), mm = JsNum.ToNumber(parts[1]), yy = JsNum.ToNumber(parts[2]);
                    if (Truthy(dd) && Truthy(mm) && Truthy(yy)) ts = JsDateValue(yy, mm - 1, dd);
                }
                dayCols.Add((i, ts, i < dataArr.Length ? dataArr[i] : null));
            }
            V8Sort.Sort(dayCols, (a, b) => a.Ts - b.Ts);
            for (var n = 0; n < dayCols.Count; n++) r.Set("Day " + (n + 1), dayCols[n].Raw ?? 0.0);
            return r;
        }

        private static bool Truthy(double d) => d != 0 && !double.IsNaN(d);

        /// <summary>_transformArticleMaster → Article Master row.</summary>
        public static JsObject TransformArticleMaster(string?[] headerArr, string?[] dataArr)
        {
            var o = RawObjFromHeaders(headerArr, dataArr);
            var r = new JsObject();
            r.Set("Generic Material", RawPick(o, "Generic Material"));
            r.Set("ART DESC", RawPickAfter(headerArr, dataArr, "Generic Material"));
            r.Set("Brand Type", RawPick(o, "Brand Type"));
            r.Set("Division", RawPick(o, "Division"));
            r.Set("MC CD", RawPick(o, "Merchandise Category"));
            r.Set("MC DESC", RawPickAfter(headerArr, dataArr, "Merchandise Category"));
            r.Set("MRP", RawPick(o, "MRP"));
            r.Set("Flag (BOGO/NON-BOGO)", RawPick(o, "Planning Flag"));
            r.Set("Style", RawPick(o, "Style"));
            r.Set("Fit", RawPick(o, "Cut & Fit"));
            r.Set("Product Material", RawPick(o, "Base Material", "Product Material"));
            r.Set("Design", RawPick(o, "Design"));
            r.Set("Vendor", RawPick(o, "Vendor"));
            r.Set("VENDOR NAME", RawPickAfter(headerArr, dataArr, "Vendor"));
            r.Set("Season", RawPick(o, "Season"));
            return r;
        }

        /// <summary>_transformBSRDetail → BSR row.</summary>
        public static JsObject TransformBSRDetail(string?[] headerArr, string?[] dataArr)
        {
            var o = RawObjFromHeaders(headerArr, dataArr);
            var r = new JsObject();
            r.Set("Division", RawPick(o, "DIV", "Division"));
            r.Set("MC CD", RawPick(o, "MC CD"));
            r.Set("MC DESC", RawPick(o, "MC DESC"));
            r.Set("MRP", RawPick(o, "MRP"));
            r.Set("Generic Material", RawPick(o, "Generic Material"));
            r.Set("Color", RawPick(o, "Color", "Colour"));
            r.Set("Brand Type", RawPick(o, "Brand Type"));
            r.Set("BOGO/NON-BOGO", RawPick(o, "Article Type", "BOGO/NON-BOGO"));
            r.Set("WEEK ON FLOOR", RawPick(o, "WEEK ON FLOOR"));
            r.Set("AVG PERDAY SELL THRU%", RawPick(o, "AVG PERDAY SELL THRU%"));
            return r;
        }

        public sealed class RawWorkbookResult
        {
            /// <summary>key → rows, in first-detection order.</summary>
            public List<KeyValuePair<string, List<SheetRow>>> ByKey { get; } = new();
            public List<IngestRawDetection> Detected { get; } = new();
            public List<IngestSkippedSheet> Skipped { get; } = new();

            public void AddRows(string key, List<SheetRow> rows)
            {
                var i = ByKey.FindIndex(k => k.Key == key);
                if (i < 0) ByKey.Add(new(key, new List<SheetRow>(rows)));
                else ByKey[i].Value.AddRange(rows);
            }
        }

        public const string SkipNoPattern = "no recognized column pattern (likely a pivot-summary tab)";

        /// <summary>_processRawWorkbook (index.html line 2503): classify every sheet and convert it.</summary>
        public static RawWorkbookResult ProcessRawWorkbook(WorkbookData wb, string filename)
        {
            var res = new RawWorkbookResult();
            foreach (var sn in wb.SheetNames)
            {
                if (!wb.Sheets.TryGetValue(sn, out var ws)) continue;
                var raw = SheetJson.ToRows(ws);
                var cls = ClassifyRawSheet(raw);
                if (cls == null)
                {
                    res.Skipped.Add(new IngestSkippedSheet(filename, sn, SkipNoPattern));
                    continue;
                }
                var headerArr = cls.HeaderRowIdx < raw.Count ? raw[cls.HeaderRowIdx] : Array.Empty<string?>();
                var dataStart = cls.HeaderRowIdx + 1;
                var outRows = new List<SheetRow>();
                string? targetKey = null;
                Func<string?[], JsObject>? tf = null;
                switch (cls.Type)
                {
                    case "po_pending": targetKey = "f2"; tf = d => TransformPOPending(headerArr, d); break;
                    case "stock": targetKey = "f1"; tf = d => TransformStock(headerArr, d); break;
                    case "sales_trend":
                    {
                        targetKey = "f3";
                        var dateRowArr = cls.DateRowIdx < raw.Count ? raw[cls.DateRowIdx] : Array.Empty<string?>();
                        tf = d => TransformSalesTrend(headerArr, dateRowArr, d);
                        break;
                    }
                    case "article_master": targetKey = "artmaster"; tf = d => TransformArticleMaster(headerArr, d); break;
                    case "bsr_detail": targetKey = "bsr"; tf = d => TransformBSRDetail(headerArr, d); break;
                }
                for (var i = dataStart; i < raw.Count; i++)
                {
                    if (raw[i].Any(v => v == null || v.Length > 0)) outRows.Add(tf!(raw[i]).ToSheetRow());
                }
                if (targetKey != null && outRows.Count > 0)
                {
                    res.AddRows(targetKey, outRows);
                    res.Detected.Add(new IngestRawDetection(filename, sn, cls.Type, targetKey, outRows.Count));
                }
            }
            return res;
        }

        // ── GRC buying history ─────────────────────────────────────────

        public sealed class GrcParseException : Exception
        {
            public GrcParseException(string message) : base(message) { }
        }

        public sealed class GrcResult
        {
            public List<SheetRow> Rows { get; } = new();
            public string PeriodType { get; set; } = "month";
            public List<string> Periods { get; set; } = new();
        }

        private static readonly Regex Period = new(@"^[0-9]{2}\.[0-9]{4}$", RegexOptions.CultureInvariant);
        private static readonly Regex TrailingDotZero = new(@"\.0$", RegexOptions.CultureInvariant);

        private static string? At(string?[] row, int i) => i >= 0 && i < row.Length ? row[i] : null;

        /// <summary>
        /// Port of loadGRCFile (index.html line 2557): reshapes the pivoted "Company Level Purchase
        /// Report" (monthly, or weekly with GRC Q / GRC V pairs) into rows
        /// {brand, gm, artdesc, colour, period, periodType, qty, value}. Throws
        /// <see cref="GrcParseException"/> with the page's messages.
        /// </summary>
        public static GrcResult ParseGrc(WorkbookData wb)
        {
            var ws = wb.SheetNames.Count > 0 && wb.Sheets.TryGetValue(wb.SheetNames[0], out var s0) ? s0 : null;
            var raw = SheetJson.ToRows(ws);
            var headerRowIdx = -1;
            for (var i = 0; i < Math.Min(raw.Count, 15); i++)
            {
                if (raw[i].Any(c => RawHdr(c) == "brand type")) { headerRowIdx = i; break; }
            }
            if (headerRowIdx < 0) throw new GrcParseException("Could not find the Brand Type / Generic Material header row.");
            var headerRow = raw[headerRowIdx];
            var brandCol = Array.FindIndex(headerRow, h => RawHdr(h) == "brand type");
            var gmCol = Array.FindIndex(headerRow, h => RawHdr(h) == "generic material");
            var colourCol = Array.FindIndex(headerRow, h => RawHdr(h).StartsWith("color", StringComparison.Ordinal) || RawHdr(h).StartsWith("colour", StringComparison.Ordinal));
            if (colourCol < 0) throw new GrcParseException("Could not find the Color/Colour column.");
            var artDescCol = -1;
            for (var c = gmCol + 1; c < colourCol; c++) { if (string.IsNullOrEmpty(At(headerRow, c))) { artDescCol = c; break; } }

            var isWeekly = RawHdr(At(headerRow, colourCol)).Contains("week");
            var res = new GrcResult();
            var grcRows = new List<(JsObject Row, string Period, string PeriodType)>();

            object Brand(string?[] row) { var b = At(row, brandCol); return string.IsNullOrEmpty(b) ? "NON-PL" : b; }
            string Gm(string?[] row) => TrailingDotZero.Replace(JsNum.Trim(At(row, gmCol)!), "", 1);
            object? ArtDesc(string?[] row) => artDescCol >= 0 ? At(row, artDescCol) : "";

            if (isWeekly)
            {
                var metricRow = headerRowIdx - 1 >= 0 ? raw[headerRowIdx - 1] : Array.Empty<string?>();
                var periodOrder = new List<string>();
                var periodCols = new Dictionary<string, (int? Q, int? V)>();
                for (var c = 0; c < headerRow.Length; c++)
                {
                    var label = JsNum.Trim(headerRow[c] ?? "");
                    if (!Period.IsMatch(label)) continue;
                    var metric = RawHdr(At(metricRow, c));
                    if (!periodCols.ContainsKey(label)) { periodCols[label] = (null, null); periodOrder.Add(label); }
                    if (metric == "grc q") periodCols[label] = (c, periodCols[label].V);
                    else if (metric == "grc v") periodCols[label] = (periodCols[label].Q, c);
                }
                if (periodOrder.Count == 0) throw new GrcParseException("No week columns (WW.YYYY) found under the week header row.");
                for (var i = headerRowIdx + 1; i < raw.Count; i++)
                {
                    var row = raw[i];
                    if (string.IsNullOrEmpty(At(row, gmCol))) continue;
                    var brand = Brand(row); var gm = Gm(row); var artdesc = ArtDesc(row); var colour = At(row, colourCol);
                    foreach (var week in periodOrder)
                    {
                        var cols = periodCols[week];
                        var qty = cols.Q != null ? ParseNum(At(row, cols.Q.Value)) : null;
                        var value = cols.V != null ? ParseNum(At(row, cols.V.Value)) : null;
                        if (qty != null && qty != 0)
                            grcRows.Add((GrcRow(brand, gm, artdesc, colour, week, "week", qty.Value, value is { } v && v != 0 ? v : null), week, "week"));
                    }
                }
            }
            else
            {
                var monthRowIdx = -1;
                for (var i = 0; i < headerRowIdx; i++)
                {
                    if (raw[i].Any(c => { var s = (c ?? "").ToLowerInvariant(); return s.Contains("cal. year") || s.Contains("cal year"); })) { monthRowIdx = i; break; }
                }
                if (monthRowIdx < 0) throw new GrcParseException("Could not find the \"Cal. year / month\" row — is this the right file?");
                var monthRow = raw[monthRowIdx];
                var monthCols = new List<(int Col, string Month)>();
                for (var c = 0; c < monthRow.Length; c++)
                {
                    var t = JsNum.Trim(monthRow[c] ?? "");
                    if (Period.IsMatch(t)) monthCols.Add((c, t));
                }
                if (monthCols.Count == 0) throw new GrcParseException("No month columns (MM.YYYY) found in the Cal. year / month row.");
                for (var i = headerRowIdx + 1; i < raw.Count; i++)
                {
                    var row = raw[i];
                    if (string.IsNullOrEmpty(At(row, gmCol))) continue;
                    var brand = Brand(row); var gm = Gm(row); var artdesc = ArtDesc(row); var colour = At(row, colourCol);
                    foreach (var mc in monthCols)
                    {
                        var qtyRaw = At(row, mc.Col);
                        if (qtyRaw == null) continue;
                        var qty = qtyRaw.Replace(",", "");
                        if (qty.Length == 0) continue;
                        var q = JsNum.ParseFloat(qty);
                        if (!double.IsNaN(q) && q != 0)
                            grcRows.Add((GrcRow(brand, gm, artdesc, colour, mc.Month, "month", q, null), mc.Month, "month"));
                    }
                }
            }
            if (grcRows.Count == 0) throw new GrcParseException("Parsed the file but found zero usable GRC quantity rows.");
            res.Rows.AddRange(grcRows.Select(r => r.Row.ToSheetRow()));
            res.PeriodType = grcRows[0].PeriodType;
            res.Periods = SortPeriods(grcRows.Select(r => r.Period).Distinct());
            return res;
        }

        /// <summary>The GRC period sort (year × 100 + number), shared with shared.js afterDataChange.</summary>
        public static List<string> SortPeriods(IEnumerable<string> periods)
        {
            var list = periods.ToList();
            static double Key(string p)
            {
                var parts = p.Split('.');
                return JsNum.ParseInt(parts.Length > 1 ? parts[1] : null) * 100 + JsNum.ParseInt(parts[0]);
            }
            V8Sort.Sort(list, (a, b) => Key(a) - Key(b));
            return list;
        }

        private static JsObject GrcRow(object brand, string gm, object? artdesc, object? colour, string period, string periodType, double qty, double? value)
        {
            var o = new JsObject();
            o.Set("brand", brand);
            o.Set("gm", gm);
            o.Set("artdesc", artdesc);
            o.Set("colour", colour);
            o.Set("period", period);
            o.Set("periodType", periodType);
            o.Set("qty", qty);
            o.Set("value", value);
            return o;
        }

        /// <summary>parseNum of the weekly GRC branch: commas removed, parseFloat; null when blank / not a number.</summary>
        private static double? ParseNum(string? raw2)
        {
            if (raw2 == null) return null;
            var s = raw2.Replace(",", "");
            var n = JsNum.ParseFloat(s);
            return s.Length > 0 && !double.IsNaN(n) ? n : null;
        }
    }
}

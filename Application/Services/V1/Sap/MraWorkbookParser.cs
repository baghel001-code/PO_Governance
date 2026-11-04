using System.Globalization;
using System.Text.RegularExpressions;
using Application.DTOs.Ingest;
using Application.Services.V1.Ingest.SheetJs;
using Domain.Entities.Sap;

namespace Application.Services.V1.Sap
{
    /// <summary>
    /// Reads the morning MRA report (Excel / CSV) into MRA lines. The layout is not fixed yet, so the columns are found by
    /// their headers (several spellings each, case / spaces / punctuation ignored) on the first sheet that has an
    /// "MRA No" header, within its first 15 rows (a group heading above the headers is fine).
    /// Required: MRA No, Qty, Status (columns and values). Every other column is optional.
    /// A file with any wrong row is refused as a whole: the stored data is only replaced by a complete file.
    /// </summary>
    public static class MraWorkbookParser
    {
        public const int MaxErrors = 50;
        private const int HeaderSearchRows = 15;

        /// <summary>Field → accepted headers (normalized: lower case, letters and digits only).</summary>
        public static readonly IReadOnlyList<(string Field, string[] Headers)> Columns = new (string, string[])[]
        {
            ("MraNo", new[] { "mrano", "mranumber", "mrnno", "mrnnumber", "mra", "mrn", "mradocno", "mradocumentno" }),
            ("Qty", new[] { "qty", "poqty", "mraqty", "quantity", "orderqty" }),
            ("Status", new[] { "status", "decision", "mrastatus" }),
            ("GenericArticle", new[] { "genericartcd", "genericartcode", "genericarticle", "genericarticlecode", "genericmaterial", "genericart", "gm" }),
            ("GenericArtDesc", new[] { "genericartdesc", "genericarticledesc", "genericartdescription", "genericarticledescription", "artdesc" }),
            ("Colour", new[] { "colour", "color", "colourcode", "colorcode", "colourcd", "colorcd" }),
            ("VendorCode", new[] { "vendorcd", "vendorcode", "vendor", "vendorno" }),
            ("VendorName", new[] { "vendorname" }),
            ("BrandType", new[] { "brandtype", "brand" }),
            ("McCode", new[] { "mccode", "mccd", "mc" }),
            ("McName", new[] { "mcname", "mcdesc", "mcdes", "mcdescription" }),
            ("Mrp", new[] { "mrp" }),
            ("PlanningFlag", new[] { "planningflag", "flag", "bogononbogo", "flagbogononbogo" }),
            ("Fit", new[] { "fit" }),
            ("Design", new[] { "design" }),
            ("Style", new[] { "style" }),
            ("Fabric", new[] { "fabric" }),
            ("ProductMaterial", new[] { "productmaterial", "material" }),
            ("PoValue", new[] { "poval", "povalue", "mraval", "mravalue", "value" }),
            ("DeliveryDate", new[] { "deliverydate", "deldate", "deliverydt", "delivery" }),
            ("ApprovalLevel", new[] { "levelwaitingforapproval", "level", "approvallevel", "waitingat" }),
            ("RequestId", new[] { "requestid", "toolrequestid" }),
            ("Remark", new[] { "remark", "remarks", "comment", "comments" }),
        };

        public static readonly string[] Required = { "MraNo", "Qty", "Status" };

        private static readonly Dictionary<string, string> Labels = new()
        {
            ["MraNo"] = "MRA No", ["Qty"] = "Qty", ["Status"] = "Status", ["GenericArticle"] = "Generic Art Cd", ["Colour"] = "Colour",
            ["DeliveryDate"] = "Delivery date", ["Mrp"] = "MRP", ["PoValue"] = "PO Val"
        };

        private static string Label(string field) => Labels.TryGetValue(field, out var l) ? l : field;

        public static string NormalizeHeader(string? h) => Regex.Replace((h ?? "").ToLowerInvariant(), "[^a-z0-9]", "");

        public sealed class Result
        {
            public List<MraLine> Lines { get; } = new();
            public List<string> Errors { get; } = new();
            public int ErrorCount { get; set; }
            public int SkippedEmpty { get; set; }
            public string? Sheet { get; set; }
            public Dictionary<string, string> Columns { get; } = new();
            public bool Ok => ErrorCount == 0 && Lines.Count > 0;
        }

        public static Result Parse(WorkbookData wb, IReadOnlyCollection<string> allowedStatuses)
        {
            var res = new Result();
            foreach (var name in wb.SheetNames)
            {
                var ws = wb.Sheets[name];
                if (FindHeader(ws) is not { } header) continue;
                res.Sheet = name;
                ReadSheet(ws, header.Row, header.Map, allowedStatuses, res);
                return res;
            }
            Error(res, "No sheet has an \"MRA No\" column header in its first " + HeaderSearchRows + " rows");
            return res;
        }

        private static (int Row, Dictionary<string, (int Col, string Header)> Map)? FindHeader(SheetData ws)
        {
            if (ws.Range is not { } r) return null;
            for (var row = r.StartRow; row <= Math.Min(r.EndRow, r.StartRow + HeaderSearchRows - 1); row++)
            {
                var map = new Dictionary<string, (int, string)>();
                for (var c = r.StartCol; c <= r.EndCol; c++)
                {
                    var text = Text(ws.Get(row, c));
                    var norm = NormalizeHeader(text);
                    if (norm.Length == 0) continue;
                    foreach (var (field, headers) in Columns)
                        if (!map.ContainsKey(field) && headers.Contains(norm)) { map[field] = (c, text!.Trim()); break; }
                }
                if (map.ContainsKey("MraNo")) return (row, map);
            }
            return null;
        }

        private static void ReadSheet(SheetData ws, int headerRow, Dictionary<string, (int Col, string Header)> map,
                                      IReadOnlyCollection<string> allowed, Result res)
        {
            foreach (var (field, (_, header)) in map) res.Columns[header] = field;
            foreach (var f in Required.Where(f => !map.ContainsKey(f)))
                Error(res, $"Column \"{Label(f)}\" is missing (required: MRA No, Qty, Status)");
            if (res.ErrorCount > 0) return;

            var seen = new Dictionary<(string, string, string), int>();
            var range = ws.Range!.Value;
            for (var row = headerRow + 1; row <= range.EndRow; row++)
            {
                var n = row + 1;   // Excel row number
                SheetCell? Cell(string f) => map.TryGetValue(f, out var m) ? ws.Get(row, m.Col) : null;
                string S(string f, int max) { var t = (Text(Cell(f)) ?? "").Trim(); return t.Length > max ? t[..max] : t; }
                if (map.Keys.All(f => (Text(Cell(f)) ?? "").Trim().Length == 0)) { res.SkippedEmpty++; continue; }

                var line = new MraLine { MraNo = Code(Cell("MraNo")), Source = "UPLOAD" };
                var ok = true;
                if (line.MraNo.Length == 0) { Error(res, $"Row {n}: MRA No is missing"); ok = false; }
                else if (line.MraNo.Length > 20) { Error(res, $"Row {n}: MRA No \"{line.MraNo}\" is longer than 20 characters"); ok = false; }

                var qty = Number(Cell("Qty"));
                if (qty == null) { Error(res, $"Row {n}: Qty is missing or not a number" + Shown(Cell("Qty"))); ok = false; }
                else if (qty < 0) { Error(res, $"Row {n}: Qty is negative ({qty})"); ok = false; }
                else line.Qty = qty.Value;

                var rawStatus = (Text(Cell("Status")) ?? "").Trim();
                var status = MraStatuses.Normalize(rawStatus, allowed);
                if (rawStatus.Length == 0) { Error(res, $"Row {n}: Status is missing"); ok = false; }
                else if (status == null) { Error(res, $"Row {n}: Status \"{rawStatus}\" is not one of {string.Join(", ", allowed)}"); ok = false; }
                else line.Status = status;

                line.GenericArticle = Code(Cell("GenericArticle"));
                line.Colour = S("Colour", 40);
                if (line.GenericArticle.Length > 40) { Error(res, $"Row {n}: Generic Art Cd is longer than 40 characters"); ok = false; }
                line.GenericArtDesc = Opt(S("GenericArtDesc", 200));
                line.VendorCode = Opt(Code(Cell("VendorCode"), 20));
                line.VendorName = Opt(S("VendorName", 200));
                line.BrandType = Opt(S("BrandType", 40));
                line.McCode = Opt(Code(Cell("McCode"), 20));
                line.McName = Opt(S("McName", 100));
                line.PlanningFlag = Opt(S("PlanningFlag", 20));
                line.Fit = Opt(S("Fit", 100));
                line.Design = Opt(S("Design", 100));
                line.Style = Opt(S("Style", 100));
                line.Fabric = Opt(S("Fabric", 100));
                line.ProductMaterial = Opt(S("ProductMaterial", 100));
                line.ApprovalLevel = Opt(S("ApprovalLevel", 30));
                line.RequestId = Opt(S("RequestId", 80));
                line.Remark = Opt(S("Remark", 500));
                if (!OptionalNumber(Cell("Mrp"), out var mrp)) { Error(res, $"Row {n}: MRP is not a number" + Shown(Cell("Mrp"))); ok = false; }
                line.Mrp = mrp;
                if (!OptionalNumber(Cell("PoValue"), out var val)) { Error(res, $"Row {n}: PO Val is not a number" + Shown(Cell("PoValue"))); ok = false; }
                line.PoValue = val;
                if (!OptionalDate(Cell("DeliveryDate"), out var del)) { Error(res, $"Row {n}: Delivery date is not a date" + Shown(Cell("DeliveryDate"))); ok = false; }
                line.DeliveryDate = del;

                if (!ok) continue;
                var key = (line.MraNo, line.GenericArticle.ToUpperInvariant(), line.Colour.ToUpperInvariant());
                if (seen.TryGetValue(key, out var first))
                {
                    Error(res, $"Row {n}: MRA {line.MraNo} / {Dash(line.GenericArticle)} / {Dash(line.Colour)} is already in row {first}");
                    continue;
                }
                seen[key] = n;
                res.Lines.Add(line);
            }
            if (res.Lines.Count == 0 && res.ErrorCount == 0) Error(res, "The file has no MRA rows below the headers");
        }

        private static string Dash(string s) => s.Length == 0 ? "-" : s;

        private static void Error(Result res, string message)
        {
            res.ErrorCount++;
            if (res.Errors.Count < MaxErrors) res.Errors.Add(message);
        }

        private static string Shown(SheetCell? c)
        {
            var t = (Text(c) ?? "").Trim();
            return t.Length == 0 ? "" : $" (\"{(t.Length > 30 ? t[..30] : t)}\")";
        }

        private static string? Opt(string s) => s.Length == 0 ? null : s;

        private static string? Text(SheetCell? c) => c == null ? null : SheetJson.CellValue(c);

        /// <summary>Codes as text: a numeric cell is written without decimals (Excel may have dropped leading zeros).</summary>
        private static string Code(SheetCell? c, int max = int.MaxValue)
        {
            string t;
            if (c is { Type: SheetCellType.Number, NoValue: false } && Math.Abs(c.Number % 1) < 1e-9)
                t = c.Number.ToString("0", CultureInfo.InvariantCulture);
            else t = (Text(c) ?? "").Trim();
            return t.Length > max ? t[..max] : t;
        }

        private static decimal? Number(SheetCell? c)
        {
            if (c is { Type: SheetCellType.Number, NoValue: false }) return (decimal)c.Number;
            var t = (Text(c) ?? "").Trim().Replace(",", "");
            return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
        }

        private static bool OptionalNumber(SheetCell? c, out decimal? value)
        {
            value = null;
            if ((Text(c) ?? "").Trim().Length == 0) return true;
            value = Number(c);
            return value != null;
        }

        private static readonly string[] DateFormats =
        {
            "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd-MMM-yyyy", "d-MMM-yyyy",
            "dd MMM yyyy", "dd-MMM-yy", "yyyyMMdd", "yyyy/MM/dd"
        };

        /// <summary>An Excel date (serial number) or text in a day-first format (dd.MM.yyyy, dd-MM-yyyy, dd/MM/yyyy, yyyy-MM-dd …).</summary>
        private static bool OptionalDate(SheetCell? c, out DateTime? value)
        {
            value = null;
            if (c is { Type: SheetCellType.Number, NoValue: false } && c.Number is > 20000 and < 80000)
            {
                value = new DateTime(1899, 12, 30).AddDays(Math.Floor(c.Number));
                return true;
            }
            var t = (Text(c) ?? "").Trim();
            if (t.Length == 0) return true;
            if (DateTime.TryParseExact(t, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) { value = d.Date; return true; }
            return false;
        }
    }
}

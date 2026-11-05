using System.Text.Json;
using ClosedXML.Excel;
using DecisionEngine.Results.Explain;

namespace Infrastructure.Services.V1.Excel
{
    /// <summary>
    /// v1.1.6 S8: writes a "How was this number worked out?" workbook (<see cref="ExplainBookDto"/>, built by
    /// DecisionEngine.Explain.SingleExplainBuilder) with live formulas — the layout of legacy/v1.1.6/app/explain.js
    /// <c>Explain.book</c>: row 1 title, row 2 who / when + the colour legend, row 4 header (navy), data from row 5,
    /// blue = data from the files / settings, black = formula, green = link to another sheet. Excel recalculates every
    /// formula when the file is opened; each formula cell also carries the tool's own value.
    /// </summary>
    public static class ExplainExcelWriter
    {
        private static readonly Dictionary<string, string> Formats = new()
        {
            ["int"] = "[>=10000000]##\\,##\\,##\\,##0;[>=100000]##\\,##\\,##0;##,##0", ["d1"] = "#,##0.0", ["d2"] = "0.00", ["d4"] = "0.0000",
            ["pct"] = "0.00%", ["pct1"] = "0.0%", ["date"] = "dd mmm yyyy", ["txt"] = "@"
        };

        /// <summary>"Downloaded 05 Nov 2026, 10:12 am by Name. Blue = data from the files or settings, black = formula, green = link to another sheet".</summary>
        public static string Stamp(DateTimeOffset now, string? userName) =>
            "Downloaded " + now.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) + ", " +
            now.ToString("hh:mm tt", System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant() + (string.IsNullOrEmpty(userName) ? "" : " by " + userName) +
            ". Blue = data from the files or settings, black = formula, green = link to another sheet";

        public static byte[] Write(ExplainBookDto book, string stamp)
        {
            using var wb = new XLWorkbook();
            foreach (var sh in book.Sheets)
            {
                var ws = wb.Worksheets.Add(sh.Name);
                var ncol = Math.Max(1, sh.Cols.Count);
                var t = ws.Cell(1, 1);
                t.Value = string.IsNullOrEmpty(sh.Title) ? book.FileName : sh.Title;
                t.Style.Font.Bold = true; t.Style.Font.FontSize = 15; t.Style.Font.FontColor = XLColor.FromHtml("#0F1B3D");
                var s2 = ws.Cell(2, 1);
                s2.Value = (string.IsNullOrEmpty(sh.Subtitle) ? "" : sh.Subtitle + ". ") + stamp;
                s2.Style.Font.FontSize = 9; s2.Style.Font.FontColor = XLColor.FromHtml("#5B6478");
                ws.Range(1, 1, 1, ncol).Merge();
                ws.Range(2, 1, 2, ncol).Merge();
                for (var c = 0; c < sh.Cols.Count; c++)
                {
                    var col = sh.Cols[c];
                    var h = ws.Cell(4, c + 1);
                    h.Value = col.Label;
                    h.Style.Font.Bold = true; h.Style.Font.FontColor = XLColor.White; h.Style.Font.FontSize = 10;
                    h.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F2A55");
                    h.Style.Alignment.Horizontal = col.Fmt != null && col.Fmt != "txt" ? XLAlignmentHorizontalValues.Right : XLAlignmentHorizontalValues.Left;
                    h.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    h.Style.Alignment.WrapText = true;
                    Border(h);
                    ws.Column(c + 1).Width = col.Width;
                }
                for (var ri = 0; ri < sh.Rows.Count; ri++)
                {
                    var row = sh.Rows[ri];
                    for (var ci = 0; ci < sh.Cols.Count; ci++)
                    {
                        var col = sh.Cols[ci];
                        var x = ci < row.Cells.Count ? row.Cells[ci] : null;
                        var cell = ws.Cell(5 + ri, ci + 1);
                        if (x == null && row.Style == null) continue;
                        if (x != null)
                        {
                            var v = Plain(x.Value);
                            if (x.Formula != null) cell.FormulaA1 = x.Formula;
                            else if (v is double d) cell.Value = d;
                            else cell.Value = v as string ?? "";
                            var fm = x.Fmt ?? col.Fmt;
                            if (fm != null && Formats.TryGetValue(fm, out var nf) && (x.Formula != null || v is double)) cell.Style.NumberFormat.Format = nf;
                        }
                        Style(cell, row.Style, ri, x?.Style, col.Width);
                    }
                }
                var last = 5 + sh.Rows.Count;
                if (!string.IsNullOrEmpty(sh.Note))
                {
                    var n = ws.Cell(last + 1, 1);
                    n.Value = sh.Note;
                    n.Style.Font.FontSize = 9; n.Style.Font.Italic = true; n.Style.Font.FontColor = XLColor.FromHtml("#5B6478");
                }
                ws.Row(1).Height = 24; ws.Row(2).Height = 16; ws.Row(3).Height = 8; ws.Row(4).Height = 30;
                if (sh.Rows.Count > 0 && sh.Cols.Count > 0) ws.Range(4, 1, 4 + sh.Rows.Count, sh.Cols.Count).SetAutoFilter();
            }
            wb.ForceFullCalculation = true;
            wb.FullCalculationOnLoad = true;
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static object? Plain(object? v) => v switch
        {
            JsonElement { ValueKind: JsonValueKind.Number } e => e.GetDouble(),
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            JsonElement => null,
            int i => (double)i,
            long l => (double)l,
            _ => v
        };

        private static void Border(IXLCell c)
        {
            var b = c.Style.Border;
            b.TopBorder = b.BottomBorder = b.LeftBorder = b.RightBorder = XLBorderStyleValues.Thin;
            b.TopBorderColor = b.BottomBorderColor = b.LeftBorderColor = b.RightBorderColor = XLColor.FromHtml("#D6DCE8");
        }

        private static void Style(IXLCell c, string? rowStyle, int ri, string? cellStyle, double width)
        {
            Border(c);
            c.Style.Font.FontSize = 10;
            c.Style.Font.FontColor = XLColor.FromHtml("#0F1B3D");
            c.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            c.Style.Alignment.WrapText = width > 34;
            if (ri % 2 == 1) c.Style.Fill.BackgroundColor = XLColor.FromHtml("#F6F8FC");
            if (rowStyle == "head") { c.Style.Font.Bold = true; c.Style.Font.FontColor = XLColor.FromHtml("#0F2F6B"); c.Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF0FB"); }
            if (rowStyle == "total") { c.Style.Font.Bold = true; c.Style.Fill.BackgroundColor = XLColor.FromHtml("#E3F4E8"); }
            if (cellStyle == "in") c.Style.Font.FontColor = XLColor.FromHtml("#0000FF");
            else if (cellStyle == "link") c.Style.Font.FontColor = XLColor.FromHtml("#008000");
            else if (cellStyle == "bold") c.Style.Font.Bold = true;
        }
    }
}

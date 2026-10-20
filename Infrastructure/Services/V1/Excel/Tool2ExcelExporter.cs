using System.IO.Compression;
using Application.Interfaces.V1.Tool2;
using ClosedXML.Excel;
using DecisionEngine.Results.Tool2;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services.V1.Excel
{
    /// <summary>
    /// Writes the Tool 2 workbook of index.html <c>exportAssXLSX</c> + <c>_writeXlsxNoGridlines</c>: same sheets,
    /// headers, column order and widths, merges, autofilters, internal hyperlinks, fonts / fills / borders /
    /// alignment and number formats; every sheet without gridlines. Numbers are numeric cells, strings text
    /// ("" cells are written as empty text like SheetJS), NaN / null are left empty.
    /// </summary>
    public sealed class Tool2ExcelExporter : ITool2ExcelExporter
    {
        public byte[] Write(Tool2WorkbookDto workbook)
        {
            using var wb = new XLWorkbook();
            foreach (var sheet in workbook.Sheets)
            {
                var ws = wb.Worksheets.Add(sheet.Name);
                ws.ShowGridLines = false;
                for (var r = 0; r < sheet.Rows.Count; r++)
                {
                    var row = sheet.Rows[r];
                    for (var c = 0; c < row.Count; c++)
                    {
                        var cell = ws.Cell(r + 1, c + 1);
                        switch (row[c])
                        {
                            case null: break;
                            case double d when double.IsFinite(d): cell.Value = d; break;
                            case double: break;
                            case int i: cell.Value = i; break;
                            case string s: cell.Value = s; break;
                            default: cell.Value = row[c]!.ToString(); break;
                        }
                    }
                }
                if (sheet.Cols != null)
                    for (var c = 0; c < sheet.Cols.Count; c++)
                    {
                        ws.Column(c + 1).Width = sheet.Cols[c].Width;
                        if (sheet.Cols[c].Hidden == true) ws.Column(c + 1).Hide();
                    }
                if (sheet.Merges != null) foreach (var m in sheet.Merges) ws.Range(m).Merge(false);
                foreach (var (addr, st) in sheet.Styles) Apply(ws.Cell(addr), st);
                if (!string.IsNullOrEmpty(sheet.AutoFilter)) ws.Range(sheet.AutoFilter).SetAutoFilter();
            }
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static void Apply(IXLCell cell, Tool2CellStyleDto st)
        {
            var s = cell.Style;
            if (st.FontColor != null) s.Font.FontColor = XLColor.FromHtml("#" + st.FontColor);
            if (st.Bold == true) s.Font.Bold = true;
            if (st.Italic == true) s.Font.Italic = true;
            if (st.Underline == true) s.Font.Underline = XLFontUnderlineValues.Single;
            if (st.Size != null) s.Font.FontSize = st.Size.Value;
            if (st.Fill != null) s.Fill.SetBackgroundColor(XLColor.FromHtml("#" + st.Fill));
            if (st.HAlign != null)
                s.Alignment.Horizontal = st.HAlign switch { "center" => XLAlignmentHorizontalValues.Center, "right" => XLAlignmentHorizontalValues.Right, _ => XLAlignmentHorizontalValues.Left };
            if (st.VAlign != null)
                s.Alignment.Vertical = st.VAlign switch { "center" => XLAlignmentVerticalValues.Center, "top" => XLAlignmentVerticalValues.Top, _ => XLAlignmentVerticalValues.Bottom };
            if (st.Wrap == true) s.Alignment.WrapText = true;
            if (st.Indent != null) s.Alignment.Indent = st.Indent.Value;
            if (st.Border != null)
            {
                var col = XLColor.FromHtml("#" + st.Border);
                s.Border.OutsideBorder = XLBorderStyleValues.Thin;
                s.Border.OutsideBorderColor = col;
            }
            if (st.NumFmt != null) s.NumberFormat.Format = st.NumFmt;
            if (st.Link != null)
            {
                cell.SetHyperlink(new XLHyperlink(st.Link.TrimStart('#'), st.LinkTooltip ?? ""));
            }
        }

        public byte[] Zip(IReadOnlyList<(string FileName, byte[] Content)> files)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                foreach (var (name, content) in files)
                {
                    var e = zip.CreateEntry(name, CompressionLevel.Optimal);
                    using var s = e.Open();
                    s.Write(content);
                }
            return ms.ToArray();
        }
    }

    public static class Tool2ExcelRegistration
    {
        /// <summary>Registers <see cref="ITool2ExcelExporter"/>.</summary>
        public static IServiceCollection AddTool2Excel(this IServiceCollection services)
        {
            services.AddSingleton<ITool2ExcelExporter, Tool2ExcelExporter>();
            return services;
        }
    }
}

using Application.Interfaces.V1.Analytics;
using ClosedXML.Excel;
using DecisionEngine.Results.Analytics.Grc;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services.V1.Excel
{
    /// <summary>
    /// "⬇ Export Row Detail" workbook (index.html exportGRCRowDetail, SheetJS aoa_to_sheet): one sheet
    /// "GRC Row Detail", header row + one row per GRC line, numbers as numeric cells, every column 16
    /// characters wide, autofilter over the whole range.
    /// </summary>
    public sealed class GrcExcelExporter : IGrcExcelExporter
    {
        public byte[] WriteRowDetail(GrcRowDetailExportDto export)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(export.SheetName);
            var colCount = export.Headers.Count;
            for (var c = 0; c < colCount; c++) ws.Cell(1, c + 1).Value = export.Headers[c];
            var r = 2;
            foreach (var row in export.Rows)
            {
                for (var c = 0; c < row.Count; c++)
                {
                    var cell = ws.Cell(r, c + 1);
                    switch (row[c])
                    {
                        case null: break;
                        case double d: cell.Value = d; break;
                        case int i: cell.Value = i; break;
                        case long l: cell.Value = l; break;
                        case bool b: cell.Value = b; break;
                        case string s: cell.Value = s; break;
                        default: cell.Value = DecisionEngine.Common.Js.ToJsString(row[c]); break;
                    }
                }
                r++;
            }
            for (var c = 1; c <= colCount; c++) ws.Column(c).Width = export.ColumnWidth;
            ws.Range(1, 1, Math.Max(1, export.Rows.Count + 1), colCount).SetAutoFilter();
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }
    }

    public static class GrcExcelRegistration
    {
        /// <summary>Registers <see cref="IGrcExcelExporter"/> (call from ServicesConfiguration).</summary>
        public static IServiceCollection AddGrcExcelExport(this IServiceCollection services)
        {
            services.AddSingleton<IGrcExcelExporter, GrcExcelExporter>();
            return services;
        }
    }
}

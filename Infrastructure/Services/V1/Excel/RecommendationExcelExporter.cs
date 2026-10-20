using Application.Interfaces.V1.Recommendation;
using ClosedXML.Excel;
using DecisionEngine.Results.Recommendation;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services.V1.Excel
{
    /// <summary>
    /// Writes the Multi Concept full-detail workbook of index.html <c>_exportEngineARecs</c> (SheetJS
    /// <c>aoa_to_sheet</c>): one sheet per <see cref="ExportSheetDto"/>, same sheet names, header row,
    /// column order and widths; numbers as numeric cells, strings as text, no styling (v1.1.4 wrote none).
    /// </summary>
    public sealed class RecommendationExcelExporter : IRecommendationExcelExporter
    {
        public byte[] Write(RecommendationExportDto export)
        {
            using var wb = new XLWorkbook();
            foreach (var sheet in export.Sheets)
            {
                var ws = wb.Worksheets.Add(sheet.Name);
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
                            case long l: cell.Value = l; break;
                            case bool b: cell.Value = b; break;
                            case string s:
                                if (s.Length > 0) cell.Value = s; // ClosedXML 0.10x stores a string as text (no number conversion)
                                break;
                            default: cell.Value = row[c]!.ToString(); break;
                        }
                    }
                }
                for (var c = 0; c < sheet.Widths.Count; c++) ws.Column(c + 1).Width = sheet.Widths[c];
            }
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }
    }

    public static class RecommendationExcelRegistration
    {
        /// <summary>Registers <see cref="IRecommendationExcelExporter"/>.</summary>
        public static IServiceCollection AddRecommendationExcel(this IServiceCollection services)
        {
            services.AddSingleton<IRecommendationExcelExporter, RecommendationExcelExporter>();
            return services;
        }
    }
}

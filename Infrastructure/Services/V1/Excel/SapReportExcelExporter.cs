using Application.Interfaces.V1.Sap;
using ClosedXML.Excel;
using Domain.Entities.Sap;

namespace Infrastructure.Services.V1.Excel
{
    /// <summary>
    /// "PO Documents" sheet: one row per answered SAP decision with its final document (MRN), dates in the business time
    /// zone, quantities as numbers, header bold and frozen, autofilter.
    /// </summary>
    public sealed class SapReportExcelExporter : ISapReportExcelExporter
    {
        private static readonly string[] Headers =
        {
            "Decided at", "Decision ID", "Request ID", "PO number", "Buyer", "Division", "Source system", "Mode", "Verdict", "Route to",
            "Items", "Order qty", "Document no", "Document type", "Document date", "Document received at", "Test"
        };

        public byte[] WriteDocumentReport(IReadOnlyList<SapDocumentReportRow> rows, int tzOffsetMinutes)
        {
            var tz = TimeSpan.FromMinutes(tzOffsetMinutes);
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("PO Documents");
            for (var c = 0; c < Headers.Length; c++) ws.Cell(1, c + 1).Value = Headers[c];
            ws.Row(1).Style.Font.Bold = true;
            var r = 2;
            foreach (var x in rows)
            {
                ws.Cell(r, 1).Value = x.DecidedAt.ToOffset(tz).ToString("yyyy-MM-dd HH:mm:ss");
                ws.Cell(r, 2).Value = x.DecisionId ?? "";
                ws.Cell(r, 3).Value = x.RequestId;
                ws.Cell(r, 4).Value = x.PoNumber ?? "";
                ws.Cell(r, 5).Value = x.BuyerUser ?? "";
                ws.Cell(r, 6).Value = x.Division ?? "";
                ws.Cell(r, 7).Value = x.SourceSystem ?? "";
                ws.Cell(r, 8).Value = x.Mode ?? "";
                ws.Cell(r, 9).Value = x.OverallVerdict ?? "";
                ws.Cell(r, 10).Value = x.RouteTo ?? "";
                ws.Cell(r, 11).Value = x.ItemCount;
                if (x.OrderQty is { } q) ws.Cell(r, 12).Value = q;
                ws.Cell(r, 13).Value = x.DocumentNo ?? "";
                ws.Cell(r, 14).Value = x.DocumentType ?? "";
                ws.Cell(r, 15).Value = x.DocumentDate ?? "";
                ws.Cell(r, 16).Value = x.DocumentReceivedAt?.ToOffset(tz).ToString("yyyy-MM-dd HH:mm:ss") ?? "";
                ws.Cell(r, 17).Value = x.IsTest ? "yes" : "";
                r++;
            }
            ws.Columns().AdjustToContents(1, Math.Min(r, 200));
            ws.SheetView.FreezeRows(1);
            ws.Range(1, 1, Math.Max(1, r - 1), Headers.Length).SetAutoFilter();
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }
    }
}

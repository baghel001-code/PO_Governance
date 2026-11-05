using Application.Services.V1.Sap;
using ClosedXML.Excel;
using Infrastructure.Services.V1.Excel;

namespace POGovernance.Tests.Mra
{
    /// <summary>The morning MRA workbook (v1.1.7): item sheet + header sheet found by their headers, quantities added up per MRA.</summary>
    public class MraWorkbookTests
    {
        private static byte[] Book(Action<IXLWorkbook> fill)
        {
            using var wb = new XLWorkbook();
            fill(wb);
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static void Row(IXLWorksheet ws, int r, params object?[] v)
        {
            for (var i = 0; i < v.Length; i++)
                if (v[i] != null) ws.Cell(r, i + 2).Value = XLCellValue.FromObject(v[i]);
        }

        private static byte[] Sample(bool withDecision = false) => Book(wb =>
        {
            // the header report first (sheet names do not matter), a title row and an empty row above the headers, as SAP exports it
            var h = wb.AddWorksheet("ZCAT_PR05");
            h.Cell(1, 1).Value = "05.11.2026   Dynamic List Display   1";
            Row(h, 4, "Document Number", "MC", "Total PO Quantity", "Delivery Date", "level", "Created By", "Created By", "Inspiration", "Purchase order", "Po Date", "Status");
            Row(h, 6, 342188, 131010023, 6000, "30.11.2026", 4, "Shishirjeet Balhara", "HT_1000600", "BM-BS", null, null, null);
            Row(h, 7, 344610, 121010002, 5600, "30.11.2026", 7, "A B", "HT_1001609", "", 4500099001, "02.11.2026", "Released");
            Row(h, 8, 999999, 121010002, 10, "30.11.2026", 1, "", "", "", null, null, null);                        // only in the header sheet
            var i = wb.AddWorksheet("Sheet1");
            Row(i, 4, "DOC.NO", "ARTICLE CODE", "Color", "Generic Article", "Generic Desc.", "TOTAL PO QTY", "DEL. DATE", "LEVEL", "CREATED BY",
                withDecision ? "Tool Decision" : "x", withDecision ? "Decision ID" : "y");
            Row(i, 6, 342188, 1120104033, "lbl", "000000001120104031", "AW26 SET", 3000, "30.11.2026", 4, "HT_1000600", withDecision ? "Auto Allow" : null, withDecision ? "D-1" : null);
            Row(i, 7, 342188, 1120104034, "LBL", 1120104031, "AW26 SET", 3000, "30.11.2026", 4, "HT_1000600");     // second site, same MRA
            Row(i, 8, "0000344610", 1120104040, "BLK", 1120104039, "", "5,600", 46356, "Self Reject", "HT_1001609"); // date as Excel serial, level as text
            Row(i, 9, 344611, 1120104050, "", 1120104049, "", 10, "30.11.2026", 1, "");                              // no colour → skipped
            Row(i, 10, 344612, 1120104060, "RED", 1120104059, "", 10, "30.11.2026", "Level", "");                     // level not readable → left out
        });

        [Fact]
        public void Reads_both_sheets_and_adds_up_the_sites()
        {
            var p = MraWorkbookParser.Parse(new ExcelWorkbookReader().Read(Sample(), "MRA.xlsx"));
            Assert.Null(p.Error);
            Assert.Equal(("Sheet1", "ZCAT_PR05"), (p.ItemSheet, p.HeaderSheet));
            Assert.Equal(new[] { "342188", "344610" }, p.Mras.Keys.OrderBy(x => x));
            var a = p.Mras["342188"];
            Assert.Equal(("1120104031", "LBL", 6000L, "2026-11-30", "4", 2), (a.Ga, a.Colour, a.Qty, a.Dd, a.Level, a.Lines));
            Assert.Equal(("Shishirjeet Balhara", "HT_1000600", "BM-BS", "AW26 SET"), (a.CreatedBy, a.CreatedById, a.Inspiration, a.GaDesc));
            Assert.Empty(a.Pos);
            var b = p.Mras["344610"];
            Assert.Equal(("7", 5600L, "2026-11-30"), (b.Level, b.Qty, b.Dd));
            Assert.Equal(("4500099001", "3", "2026-11-02"), (b.Pos.Single().Po, b.Pos.Single().Status, b.Pos.Single().PoDate));
            Assert.Equal((5, 3, 1, 1, 1, 1), (p.RowsItem, p.RowsHeader, p.Skipped, p.HeaderOnly, p.Invalid, p.ItemOnly));
        }

        [Fact]
        public void Optional_decision_columns_are_read()
        {
            var p = MraWorkbookParser.Parse(new ExcelWorkbookReader().Read(Sample(withDecision: true), "MRA.xlsx"));
            Assert.Equal(("Auto Allow", "D-1"), (p.Mras["342188"].DecisionInfo!.Decision, p.Mras["342188"].DecisionInfo!.DecisionId));
            Assert.Null(p.Mras["344610"].DecisionInfo);
        }

        [Fact]
        public void A_workbook_without_the_two_sheets_is_refused()
        {
            var only = Book(wb => Row(wb.AddWorksheet("x"), 1, "DOC.NO", "ARTICLE CODE", "Color", "Generic Article", "TOTAL PO QTY", "LEVEL", "DEL. DATE"));
            Assert.StartsWith("The header sheet was not found", MraWorkbookParser.Parse(new ExcelWorkbookReader().Read(only, "a.xlsx")).Error);
            var none = Book(wb => Row(wb.AddWorksheet("x"), 1, "MRA No", "Qty", "Status"));
            Assert.StartsWith("The item-lines sheet was not found", MraWorkbookParser.Parse(new ExcelWorkbookReader().Read(none, "a.xlsx")).Error);
        }

        /// <summary>A real SAP export (.xlsb), when POGOV_MRA_SAMPLE points to one (not kept in the repository).</summary>
        [Fact]
        public void Real_sap_export_when_available()
        {
            var path = Environment.GetEnvironmentVariable("POGOV_MRA_SAMPLE");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var p = MraWorkbookParser.Parse(new ExcelWorkbookReader().Read(File.ReadAllBytes(path), Path.GetFileName(path)));
            Assert.Null(p.Error);
            Assert.True(p.Mras.Count > 0);
            Console.WriteLine($"MRA sample: {p.Mras.Count} MRAs, item rows {p.RowsItem}, header rows {p.RowsHeader}, skipped {p.Skipped}, mixed {p.Mixed}, " +
                              $"header only {p.HeaderOnly}, item only {p.ItemOnly}, level diff {p.LevelDiff}, invalid {p.Invalid}; levels " +
                              string.Join(" ", p.Mras.Values.GroupBy(m => m.Level).OrderBy(g => g.Key).Select(g => g.Key + ":" + g.Count())));
        }
    }
}

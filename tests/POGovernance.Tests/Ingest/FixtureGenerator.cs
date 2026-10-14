using ClosedXML.Excel;

namespace POGovernance.Tests.Ingest
{
    /// <summary>
    /// Writes the Excel-style .xlsx fixtures (shared strings, styles, number formats, dimension) with
    /// ClosedXML. Runs only with POGOV_REGEN_FIXTURES=1; afterwards run
    /// <c>node tests/parity/ingest/make-fixtures.js</c> and <c>node tests/parity/ingest/run-parity.js</c>
    /// to rebuild the SheetJS-made fixtures and the expected JSON.
    /// </summary>
    public class FixtureGenerator
    {
        [Fact]
        public void Regenerate_xlsx_fixtures()
        {
            if (Environment.GetEnvironmentVariable("POGOV_REGEN_FIXTURES") != "1") return;
            Directory.CreateDirectory(IngestTestPaths.Fixtures);
            F1Portal(); AttrRel(); Bsr("BSR_Kids.xlsx", "KIDS"); Bsr("BSR_Ladies.xlsx", "LADIES");
            PoRaw(); StockRaw(); SalesRaw(); ArticleRaw(); BsrRaw(); GrcMonthly(); GrcWeekly();
        }

        private static void Save(XLWorkbook wb, string name) => wb.SaveAs(IngestTestPaths.Fixture(name));

        private static void Row(IXLWorksheet ws, int r, params object?[] vals)
        {
            for (var i = 0; i < vals.Length; i++)
            {
                var c = ws.Cell(r, i + 1);
                switch (vals[i])
                {
                    case null: break;
                    case string s: c.Value = s; break;
                    case double d: c.Value = d; break;
                    case int n: c.Value = n; break;
                    case long l: c.Value = l; break;
                    case bool b: c.Value = b; break;
                    case DateTime dt: c.Value = dt; break;
                    case XLError e: c.Value = e; break;
                }
            }
        }

        private static void F1Portal()
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("STOCK");
            ws.Cell("A1").Value = "Stock report as of";
            ws.Cell("B1").Value = new DateTime(2026, 6, 1);
            Row(ws, 3, "DIV", " Brand Type ", "MC CD", "MRP", "Generic Material", "Color", "OP STK", "OP STK", "2025", "Pct", "Date A", "Date B", "Flag", "Err", "", "Amount", "Small", "Sci");
            Row(ws, 4, "MENS", "PL", 1012, 499, 1234567890123L, "NAVY", 10, 11, "x", 0.125, new DateTime(2026, 6, 15), 46174.5, true, XLError.NoValueAvailable, null, 1234567.891, 0.000123456, 12345678901234567890.0);
            Row(ws, 5, "MENS", "NON-PL", "1013", 0.1 + 0.2, 4000012345L, " RED ", -5, 0, 2025, 0.5, new DateTime(2025, 12, 31), 45000.75, false, XLError.NullValue, null, -9876.5, 1e-7, 123456789012.0);
            Row(ws, 6);
            Row(ws, 7, "LADIES", "PL", 1014, 1299.5, 40000123456L, "BLACK", 1.5, 2, "y", 1, new DateTime(2024, 2, 29), 60, true, XLError.DivisionByZero, "orphan", 0, 2.5, 3.14159265358979);
            ws.Range("J4:J7").Style.NumberFormat.Format = "0.00%";
            ws.Range("K4:K7").Style.NumberFormat.Format = "dd-mm-yyyy";
            ws.Range("L4:L7").Style.NumberFormat.NumberFormatId = 14;
            ws.Range("P4:P7").Style.NumberFormat.Format = "#,##0.00";
            ws.Range("G4:G7").Style.NumberFormat.Format = "0";
            ws.Range("D4:D7").Style.NumberFormat.Format = "#,##0";
            ws.Range("Q4:Q7").Style.NumberFormat.Format = "0.0000";
            ws.Cell("B1").Style.NumberFormat.Format = "d-mmm-yy";
            ws.Cell("T20").Style.Fill.BackgroundColor = XLColor.Yellow; // formatting-only cell widens the dimension
            Save(wb, "F1_STOCK_portal.xlsx");
        }

        private static void AttrRel()
        {
            using var wb = new XLWorkbook();
            var d = wb.AddWorksheet("Sheet1");
            Row(d, 1, "lookup"); Row(d, 2, "a"); Row(d, 3, "b");
            var ws = wb.AddWorksheet("ATTR");
            Row(ws, 1, "Brand Type", "Division", "MC CD", "Assessment Dimension", "Relevant");
            Row(ws, 2, "PL", "MENS", 1012, "Style", "Y");
            Row(ws, 3, "PL", "MENS", 1012, "Fit", "N");
            Save(wb, "AttrRel_master.xlsx");
        }

        private static void Bsr(string name, string div)
        {
            using var wb = new XLWorkbook();
            for (var s = 0; s < 2; s++)
            {
                var ws = wb.AddWorksheet(div + (s == 0 ? " SS26" : " AW25"));
                Row(ws, 1, "Division", "MC CD", "Generic Material", "Color", "WEEK ON FLOOR", "AVG PERDAY SELL THRU%");
                for (var i = 0; i < 3; i++)
                    Row(ws, 2 + i, div, 2000 + i, 5000000 + s * 10 + i, i == 1 ? "RED" : "BLUE", 4 + i, 0.0125 * (i + 1));
                ws.Range("F2:F4").Style.NumberFormat.Format = "0.00%";
            }
            Save(wb, name);
        }

        private static void PoRaw()
        {
            using var wb = new XLWorkbook();
            var p = wb.AddWorksheet("TOP BS CRITERIA");
            Row(p, 1, "Row Labels", "Sum of Qty"); Row(p, 2, "PL", 100);
            var ws = wb.AddWorksheet("PO DETAIL");
            Row(ws, 1, "Pending PO report");
            Row(ws, 2, "PO", "Division", "MC CD", "MC DESC", "MRP", "GEN ART", "Brand Type", "BOGO/NON-BOGO", "Colour", "Delivery Month", "Vendor Name", "Sum of Pending Qty (Each)", "Sum of Pending Value");
            Row(ws, 3, 4500001234L, "MENS", 1012, "T-SHIRT", 499, 1234567890, "PL", "NON-BOGO", "NAVY", "06.2026", "ACME", 120, 59880);
            Row(ws, 4, 4500001235L, "MENS", 1012, "T-SHIRT", 499, 1234567891, "PL", "BOGO", "", "07.2026", "ACME", 0, 0);
            Row(ws, 5);
            Row(ws, 6, 4500001236L, "LADIES", 1014, "KURTA", 799, "1234567892", "NON-PL", "NON-BOGO", "RED", "06.2026", "BETA", 1500.5, 1199000.25);
            ws.Range("M3:M6").Style.NumberFormat.Format = "#,##0.00";
            Save(wb, "PO_raw.xlsx");
        }

        private static void StockRaw()
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Stock");
            Row(ws, 1, "Division", "Brand Type", "Merchandise Category", "", "MRP", "Planning Flag", "Generic Material", "Color", "Stk q\n+ In Q", "Intransit Qty");
            Row(ws, 2, "MENS", "PL", 1012, "T-SHIRT", 499, "NON-BOGO", 1234567890, "NAVY", 35, 5);
            Row(ws, 3, "MENS", "PL", 1012, "T-SHIRT", 499, "NON-BOGO", 1234567890, "WHITE", 0, 0);
            Save(wb, "Stock_raw.xlsx");
        }

        private static void SalesRaw()
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Sales");
            Row(ws, 1, "", "", "", "", "", "", "", "Calendar Day", "02.06.2026", "01.06.2026", new DateTime(2026, 6, 3), "bad", "30.05.2026");
            ws.Cell(1, 11).Style.NumberFormat.Format = "dd.mm.yyyy";
            Row(ws, 2, "Division", "Brand Type", "Merchandise Category", "", "MRP", "Planning Flag", "Generic Material", "Color", "Sales Qty", "Sales Qty", "Sales Qty", "Sales Qty", "Sales Qty");
            Row(ws, 3, "MENS", "PL", 1012, "T-SHIRT", 499, "NON-BOGO", 1234567890, "NAVY", 2, 1, 3, 9, "");
            Row(ws, 4, "MENS", "PL", 1012, "T-SHIRT", 499, "NON-BOGO", 1234567890, "RED", "", 4, 0, 1, 7);
            Save(wb, "Sales_raw.xlsx");
        }

        private static void ArticleRaw()
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Article");
            Row(ws, 1, "Generic Material", "", "Brand Type", "Division", "Merchandise Category", "", "MRP", "Planning Flag", "Style", "Cut & Fit", "Base Material", "Design", "Vendor", "", "Season");
            Row(ws, 2, 1234567890, "Crew neck tee", "PL", "MENS", 1012, "T-SHIRT", 499, "NON-BOGO", "CASUAL", "SLIM", "COTTON", "SOLID", 70001, "ACME LTD", "SS26");
            Row(ws, 3, 1234567891, "Polo", "PL", "MENS", 1012, "T-SHIRT", 599, "BOGO", "", "REGULAR", "", "STRIPE", 70002, "", "AW25");
            Save(wb, "ArticleMaster_raw.xlsx");
        }

        private static void BsrRaw()
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("BSR DETAIL");
            Row(ws, 1, "BSR detail");
            Row(ws, 2);
            Row(ws, 3, "DIV", "MC CD", "MC DESC", "MRP", "Generic Material", "Colour", "Brand Type", "Article Type", "WEEK\nON\nFLOOR", "AVG PERDAY SELL THRU%");
            Row(ws, 4, "MENS", 1012, "T-SHIRT", 499, 1234567890, "NAVY", "PL", "NON-BOGO", 6, 0.0234);
            Row(ws, 5, "MENS", 1012, "T-SHIRT", 499, 1234567891, "RED", "PL", "BOGO", 2, 0.5);
            ws.Range("J4:J5").Style.NumberFormat.Format = "0.00%";
            Save(wb, "BSR_detail_raw.xlsx");
        }

        private static void GrcMonthly()
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("GRC");
            Row(ws, 1, "Company Level Purchase Report");
            Row(ws, 2, "Cal. year / month", "", "", "", "04.2026", "05.2026", "06.2026");
            Row(ws, 3, "Brand Type", "Generic Material", "", "Color", "GRC Q", "GRC Q", "GRC Q");
            Row(ws, 4, "PL", 1234567890, "Crew tee", "NAVY", 1234, 0, 56.5);
            Row(ws, 5, "", "1234567891.0", "Polo", "RED", "", 7, -3);
            Row(ws, 6, "Result", "", "", "", 1234, 7, 53.5);
            ws.Range("E4:G6").Style.NumberFormat.Format = "#,##0";
            Save(wb, "GRC_monthly.xlsx");
        }

        private static void GrcWeekly()
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("GRC");
            Row(ws, 1, "", "", "", "", "GRC Q", "GRC V", "GRC Q", "GRC V", "GRC Q");
            Row(ws, 2, "Brand Type", "Generic Material", "", "Color | Calendar year / week", "15.2026", "15.2026", "14.2026", "14.2026", "16.2026");
            Row(ws, 3, "PL", 1234567890, "Crew tee", "NAVY", 10, 4990, 5, 0, "");
            Row(ws, 4, "NON-PL", 1234567891, "", "RED", 0, 100, "1,200", "5,98,800", 3);
            ws.Range("E3:I4").Style.NumberFormat.Format = "#,##0";
            Save(wb, "GRC_weekly.xlsx");
        }
    }
}

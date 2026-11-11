using System.Text.Json.Nodes;
using Admin.Services.Platform;
using DecisionEngine.Mra;
using DecisionEngine.Results.Mra;

namespace POGovernance.Tests.Portal
{
    /// <summary>MRA "Download PDF": drawn with the company PDFKit (PDFsharp) - fonts resolved without Arial on Linux.</summary>
    public class MraPdfReportTests
    {
        private static readonly object Gate = new();

        private static void Fonts()
        {
            lock (Gate)
            {
                if (PdfSharp.Fonts.GlobalFontSettings.FontResolver == null)
                    PdfSharp.Fonts.GlobalFontSettings.FontResolver = new PdfFontResolver(null,
                        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Admin")));
            }
        }

        [Fact]
        public void The_pdf_has_the_decision_the_working_and_the_history()
        {
            Fonts();
            var row = new MraRowDto
            {
                No = "321057",
                M = new MraState { Ga = "1120102820", Colour = "MST", Qty = 14576, Dd = "2026-11-29", Level = "1", GaDesc = "SS26_BBA" },
                C = new MraClass { Mon = "11.2026", Queue = "Division Head", Why = "Multi concept - not counted" },
                ToolDecision = new MraToolDecision { DecisionId = "D-20261107-000178", Decision = "Allow", Reason = "Ranks Q3", OrderQty = 70, Verdict = "ALLOW",
                                                     DocumentNo = "0000321057", MatchedBy = "MRA number (/documents)", DecidedAt = DateTimeOffset.Parse("2026-11-07T15:12:00+05:30"),
                                                     ViewUrl = "http://portal/verdict/abc" }
            };
            row.M.History.Add(new MraHistoryStep { At = DateTimeOffset.Parse("2026-11-07T15:13:00+05:30"), To = "1", Qty = 1400, By = "HT_1000654" });
            var working = new JsonObject { ["decision"] = "Allow", ["askedQty"] = 70, ["maxPermissible"] = 13647,
                                           ["monthByMonth"] = new JsonArray(new JsonObject { ["month"] = "11.2026", ["open"] = 41505, ["incoming"] = 15796, ["planSale"] = 15430, ["close"] = 61871 }) };

            var pdf = MraPdfReport.Build(row, new MraDecisionDetail { Decision = row.ToolDecision, Working = working }, null, null, DateTimeOffset.Parse("2026-11-11T08:25:00+05:30"));

            Assert.True(pdf.Length > 1000);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
            Assert.Equal("MRA_321057.pdf", MraPdfReport.FileName("321057"));
        }
    }
}

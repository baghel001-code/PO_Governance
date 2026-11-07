using Application.Services.V1.Sap;
using DecisionEngine.Mra;
using DecisionEngine.Results.Mra;

namespace POGovernance.Tests.Mra
{
    /// <summary>v8: an MRA is linked to the tool's own saved SAP check only by the MRA number SAP sent to /documents.</summary>
    public class MraDecisionLinkTests
    {
        private static MraToolDecision D(long id, string decidedAt, double qty, string? doc = null, string? po = null, string colour = "BLK", string mon = "12.2026") => new()
        {
            ItemId = id, DecisionId = "D-" + id, Gm = "1112045675", Colour = colour, DeliveryMonth = mon, OrderQty = qty, PoNumber = po, DocumentNo = doc,
            Decision = "Auto Allow", Reason = "Colour STR within limit", MaxPermissible = 22803, HasWorking = true,
            DecidedAt = DateTimeOffset.Parse(decidedAt), ViewUrl = "http://portal/verdict/abc"
        };

        private static MraState M(string? createdOn, long qty = 3000, string? po = null) => new()
        {
            Ga = "1112045675", Colour = "BLK", Qty = qty, Dd = "2026-12-20", Level = "1", CreatedOn = createdOn, PoNo = po
        };

        [Fact]
        public void Only_the_mra_number_sent_with_documents_links()
        {
            var idx = MraDecisionLinker.Index(new[]
            {
                D(1, "2026-11-04T10:00:00+05:30", 3000),                          // same article · colour · month, same qty: not a link
                D(2, "2026-11-03T10:00:00+05:30", 3000, po: "4500099001"),        // same PO: not a link
            });
            Assert.Null(MraDecisionLinker.Link(M("2026-11-05", po: "4500099001"), "900201", "12.2026", idx));
        }

        [Fact]
        public void The_mra_number_picks_the_decision_and_its_item()
        {
            var d1 = D(1, "2026-11-04T10:00:00+05:30", 3000);
            var d2 = D(2, "2026-10-01T10:00:00+05:30", 1200, colour: "WHT");                      // other colour of the same decision
            var d3 = D(3, "2026-10-01T10:00:00+05:30", 800);                                      // this MRA's item
            d2.DecisionId = d3.DecisionId = "D-77"; d2.DocumentNo = d3.DocumentNo = "0000900201";
            var idx = MraDecisionLinker.Index(new[] { d1, d2, d3 });
            var link = MraDecisionLinker.Link(M("2026-11-05"), "900201", "12.2026", idx)!;
            Assert.Equal((3L, MraDecisionLinker.ByMraNumber, "0000900201"), (link.ItemId, link.MatchedBy, link.DocumentNo));
            Assert.Equal("http://portal/verdict/abc", link.ViewUrl);
            Assert.Null(MraDecisionLinker.Link(M("2026-11-05"), "900299", "12.2026", idx));     // no document with this number
        }

        [Fact]
        public void Other_article_or_colour_still_links_and_an_unsafe_link_is_dropped()
        {
            var d = D(1, "2026-11-01T10:00:00+05:30", 500, doc: "900201", colour: "WHT", mon: "01.2027");
            d.ViewUrl = "javascript:alert(1)";
            var link = MraDecisionLinker.Link(M("2026-11-05"), "900201", "12.2026", MraDecisionLinker.Index(new[] { d }))!;
            Assert.Equal(1, link.ItemId);
            Assert.Null(link.ViewUrl);
        }
    }
}

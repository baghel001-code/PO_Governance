using Application.Services.V1.Sap;
using DecisionEngine.Mra;
using DecisionEngine.Results.Mra;

namespace POGovernance.Tests.Mra
{
    /// <summary>v8: an MRA is linked to the tool's own saved SAP check (SAP does not send the decision back).</summary>
    public class MraDecisionLinkTests
    {
        private static MraToolDecision D(long id, string decidedAt, double qty, string? po = null, string colour = "BLK", string mon = "12.2026") => new()
        {
            ItemId = id, DecisionId = "D-" + id, Gm = "1112045675", Colour = colour, DeliveryMonth = mon, OrderQty = qty, PoNumber = po,
            Decision = "Auto Allow", Reason = "Colour STR within limit", MaxPermissible = 22803, HasWorking = true,
            DecidedAt = DateTimeOffset.Parse(decidedAt), ViewUrl = "http://portal/verdict/abc"
        };

        private static MraState M(string? createdOn, long qty = 3000, string? po = null) => new()
        {
            Ga = "1112045675", Colour = "BLK", Qty = qty, Dd = "2026-12-20", Level = "1", CreatedOn = createdOn, PoNo = po
        };

        [Fact]
        public void The_latest_check_before_the_mra_with_the_same_article_colour_and_month()
        {
            var idx = MraDecisionLinker.Index(new[]
            {
                D(1, "2026-11-01T10:00:00+05:30", 2000),
                D(2, "2026-11-03T10:00:00+05:30", 2500),
                D(3, "2026-11-09T10:00:00+05:30", 3000),                          // after the MRA: never used
                D(4, "2026-11-02T10:00:00+05:30", 2000, colour: "WHT"),           // other colour
                D(5, "2026-11-02T10:00:00+05:30", 2000, mon: "01.2027"),          // other month
            });
            var d = MraDecisionLinker.Link(M("2026-11-05"), "12.2026", idx)!;
            Assert.Equal((2L, "article · colour · month"), (d.ItemId, d.MatchedBy));
            Assert.Equal("http://portal/verdict/abc", d.ViewUrl);
            Assert.Null(MraDecisionLinker.Link(M("2026-10-30"), "12.2026", idx));   // no check before it
        }

        [Fact]
        public void Equal_quantity_then_the_po_number_win()
        {
            var idx = MraDecisionLinker.Index(new[]
            {
                D(1, "2026-11-01T10:00:00+05:30", 3000),
                D(2, "2026-11-03T10:00:00+05:30", 2500),
                D(3, "2026-10-20T10:00:00+05:30", 999, po: "0004500099001"),
            });
            Assert.Equal(1, MraDecisionLinker.Link(M("2026-11-05"), "12.2026", idx)!.ItemId);                       // same qty as the MRA
            var byPo = MraDecisionLinker.Link(M("2026-11-05", po: "4500099001"), "12.2026", idx)!;
            Assert.Equal((3L, "PO number"), (byPo.ItemId, byPo.MatchedBy));
        }

        [Fact]
        public void Generic_article_of_18_digits_and_an_unsafe_link()
        {
            var d = D(1, "2026-11-01T10:00:00+05:30", 3000);
            d.Gm = "000000001112045675";
            d.ViewUrl = "javascript:alert(1)";
            var link = MraDecisionLinker.Link(M("2026-11-05"), "12.2026", MraDecisionLinker.Index(new[] { d }))!;
            Assert.Null(link.ViewUrl);
        }
    }
}

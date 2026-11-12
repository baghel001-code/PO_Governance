using Application.Services.V1.Sap;

namespace POGovernance.Tests.Mra
{
    /// <summary>
    /// Decision links: view_url of /decisions = the MRA page &lt;portal&gt;/mra-verdict/&lt;token&gt;; GET /verdict-url =
    /// &lt;portal&gt;/mra-verdict/&lt;token&gt;/&lt;mra&gt;; the PO verdict page &lt;portal&gt;/verdict/&lt;token&gt; (older view_url).
    /// </summary>
    public class MraVerdictLinksTests
    {
        [Fact]
        public void The_decision_answer_links_the_mra_page_of_its_token()
        {
            Assert.Equal("http://192.168.0.175:8177/mra-verdict/Tok_1", MraVerdictLinks.DecisionPage("http://192.168.0.175:8177/", "Tok_1"));
        }

        [Theory]
        [InlineData("http://192.168.0.175:8177/mra-verdict/EQJnCvEGrHg9-PNPreztNL4ep", "348404", "http://192.168.0.175:8177/mra-verdict/EQJnCvEGrHg9-PNPreztNL4ep/348404")]
        [InlineData("http://192.168.0.175:8177/verdict/EQJnCvEGrHg9-PNPreztNL4ep", "348404", "http://192.168.0.175:8177/mra-verdict/EQJnCvEGrHg9-PNPreztNL4ep/348404")]
        [InlineData("https://pogov.example/app/verdict/Tok_123/", "900201", "https://pogov.example/app/mra-verdict/Tok_123/900201")]
        public void The_mra_page_is_on_the_portal_of_the_decision_link(string viewUrl, string mra, string expected)
        {
            Assert.Equal(expected, MraVerdictLinks.PageUrl(viewUrl, mra));
        }

        [Theory]
        [InlineData("http://portal/mra-verdict/Tok_1", "http://portal/verdict/Tok_1")]
        [InlineData("http://portal/mra-verdict/Tok_1/348404", "http://portal/verdict/Tok_1")]
        [InlineData("http://portal/verdict/Tok_1", "http://portal/verdict/Tok_1")]
        public void The_po_verdict_page_of_any_decision_link(string viewUrl, string expected)
        {
            Assert.Equal(expected, MraVerdictLinks.PoVerdictUrl(viewUrl));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("http://portal/other/abc")]
        [InlineData("javascript:alert(1)//verdict/abc")]
        [InlineData("http://portal/verdict/")]
        [InlineData("http://portal/mra-verdict/")]
        public void No_link_without_a_usable_decision_link(string? viewUrl)
        {
            Assert.Null(MraVerdictLinks.PageUrl(viewUrl, "348404"));
            Assert.Null(MraVerdictLinks.PoVerdictUrl(viewUrl));
        }

        [Fact]
        public void The_token_of_a_decision_link()
        {
            Assert.Equal("abc_DEF-1", MraVerdictLinks.Token("http://portal/verdict/abc_DEF-1?x=1"));
            Assert.Equal("abc_DEF-1", MraVerdictLinks.Token("http://portal/mra-verdict/abc_DEF-1"));
            Assert.Equal("abc_DEF-1", MraVerdictLinks.Token("http://portal/mra-verdict/abc_DEF-1/348404"));
            Assert.Null(MraVerdictLinks.Token("http://portal/verdict/a/b"));
        }
    }
}

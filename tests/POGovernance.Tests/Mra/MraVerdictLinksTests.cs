using Application.Services.V1.Sap;

namespace POGovernance.Tests.Mra
{
    /// <summary>GET /api/sap/v1/verdict-url: the MRA page link is built from the decision's verdict link (same portal, token).</summary>
    public class MraVerdictLinksTests
    {
        [Theory]
        [InlineData("http://192.168.0.175:8177/verdict/EQJnCvEGrHg9-PNPreztNL4ep", "348404", "http://192.168.0.175:8177/mra-verdict/EQJnCvEGrHg9-PNPreztNL4ep/348404")]
        [InlineData("https://pogov.example/app/verdict/Tok_123/", "900201", "https://pogov.example/app/mra-verdict/Tok_123/900201")]
        public void The_mra_page_is_on_the_portal_of_the_verdict_link(string viewUrl, string mra, string expected)
        {
            Assert.Equal(expected, MraVerdictLinks.PageUrl(viewUrl, mra));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("http://portal/other/abc")]
        [InlineData("javascript:alert(1)//verdict/abc")]
        [InlineData("http://portal/verdict/")]
        public void No_link_without_a_usable_verdict_link(string? viewUrl)
        {
            Assert.Null(MraVerdictLinks.PageUrl(viewUrl, "348404"));
        }

        [Fact]
        public void The_token_is_the_last_part_of_the_verdict_link()
        {
            Assert.Equal("abc_DEF-1", MraVerdictLinks.Token("http://portal/verdict/abc_DEF-1?x=1"));
            Assert.Null(MraVerdictLinks.Token("http://portal/verdict/a/b"));
        }
    }
}

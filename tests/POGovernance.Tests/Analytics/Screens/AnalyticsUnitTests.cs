using System.Text.Json.Nodes;
using Application.Services.V1.Analytics.Ai;
using Application.Services.V1.Analytics.Common;
using Application.Services.V1.Analytics.Help;

namespace POGovernance.Tests.Analytics.Screens
{
    public sealed class AnalyticsUnitTests
    {
        [Theory]
        [InlineData(-0.04, 1, "-0.0")]
        [InlineData(-0.0, 1, "0.0")]
        [InlineData(2.45, 1, "2.5")]
        [InlineData(-5.25, 1, "-5.3")]
        public void ToFixedKeepsJsSign(double x, int d, string expected) => Assert.Equal(expected, AnalyticsJs.ToFixed(x, d));

        [Fact]
        public void StringifyMatchesJson()
        {
            var o = JsonNode.Parse("{\"b\":1.50,\"10\":\"x\\u0001\\\"é\",\"2\":null,\"a\":[true,1e3]}")!;
            Assert.Equal("{\"2\":null,\"10\":\"x\\u0001\\\"é\",\"b\":1.5,\"a\":[true,1000]}", AnalyticsJs.Stringify(o));
        }

        [Theory]
        [InlineData("```json\n{\"findings\":[]}\n```", true)]
        [InlineData("text {\"a\":1} more", true)]
        [InlineData("} nothing {", false)]
        [InlineData("", false)]
        [InlineData("{bad json}", false)]
        public void ParseJson(string raw, bool ok) => Assert.Equal(ok, AiInsightsBuilder.ParseJson(raw) != null);

        [Fact]
        public void HelpDefaults()
        {
            var t = HelpContentService.ComputeFor(75, 5);
            Assert.Equal("78.75", t.EffT);
            Assert.Equal("78.75×379−87,265", t.Calc);
            Assert.Equal("MAX(0, −57,419) = 0", t.Result);
        }
    }
}

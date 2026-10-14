using Domain.Entities.Common;

namespace POGovernance.Tests
{
    public class JsCompatTests
    {
        [Theory]
        [InlineData("12abc", 12)]
        [InlineData("  3.5", 3.5)]
        [InlineData(".5", 0.5)]
        [InlineData("-1e3x", -1000)]
        public void ParseFloat_matches_js(string s, double expected) => Assert.Equal(expected, Js.ParseFloat(s));

        [Fact]
        public void ParseFloat_nan() => Assert.True(double.IsNaN(Js.ParseFloat("abc")));

        [Theory]
        [InlineData(2.5, 3)]
        [InlineData(-2.5, -2)]
        [InlineData(0.49999, 0)]
        public void Round_matches_js(double x, double expected) => Assert.Equal(expected, Js.Round(x));

        [Theory]
        [InlineData(1234567, "12,34,567")]
        [InlineData(999, "999")]
        [InlineData(-1234.4, "-1,234")]
        public void Fmt_en_IN(double v, string expected) => Assert.Equal(expected, Js.Fmt(v));

        [Fact]
        public void Col_is_header_tolerant()
        {
            var r = new SheetRow(new[] { new KeyValuePair<string, object?>("Generic\nMaterial ", 12345.0), new("MRP", "") });
            Assert.Equal("12345", r.Col("generic material"));
            Assert.Equal("", r.Col("MRP"));
        }
    }
}

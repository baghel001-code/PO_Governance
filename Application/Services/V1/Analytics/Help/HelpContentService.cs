using Application.DTOs.Analytics.Help;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Analytics;
using Domain.Entities.Common;
using Domain.Entities.Engine;

namespace Application.Services.V1.Analytics.Help
{
    /// <summary>Port of index.html <c>renderEngineBHelp</c> / <c>computeFor</c> / <c>fmtNumIN</c> (lines 1948–1985).</summary>
    public sealed class HelpContentService : IHelpContentService
    {
        public const double SampleStock = 87265, SampleRate = 379;

        /// <summary>The tabs of the How-it-works view, in button order (hwTab ids).</summary>
        public static readonly IReadOnlyList<HelpTabDto> Tabs = new List<HelpTabDto>
        {
            new("vision", "🗺 Vision Overview", "help/hw-vision.html"),
            new("t1-engA", "Tool 1 · Multi Concept MRP", "help/hw-t1-engA.html"),
            new("t1-engB", "Tool 1 · Single Concept MRP · Colour STR", "help/hw-t1-engB.html"),
            new("t2-engA", "Tool 2 · Multi Concept MRP · PO Assessment", "help/hw-t2-engA.html"),
            new("t2-engB", "Tool 2 · Single Concept MRP · STR Assessment", "help/hw-t2-engB.html"),
            new("t2-export", "Tool 2 · Recommendations & Excel Export", "help/hw-t2-export.html"),
            new("access", "Sign-in, Approvals & Today's Requests", "help/hw-access.html")
        };

        private readonly IDatasetCache _cache;

        public HelpContentService(IDatasetCache cache) => _cache = cache;

        public async Task<HelpParamsDto> GetParamsAsync(CancellationToken ct = default) =>
            Compute((await _cache.GetCurrentAsync(ct)).Params);

        private static string S(double v) => Js.NumberToString(v);

        /// <summary>Port of <c>computeFor(T)</c>.</summary>
        public static HelpMaxPermExampleDto ComputeFor(double t, double buf)
        {
            var effT = t * (1 + buf / 100);
            var effTStr = S(Js.Round(effT * 100) / 100);
            var raw = effT * SampleRate - SampleStock;
            var maxPerm = Math.Max(0, Js.Round(raw));
            return new HelpMaxPermExampleDto
            {
                T = S(t), EffT = effTStr, MaxPermissible = maxPerm,
                Calc = effTStr + "×" + S(SampleRate) + "−" + Js.Fmt(SampleStock),
                Result = raw < 0 ? "MAX(0, −" + Js.Fmt(Math.Abs(raw)) + ") = 0" : "MAX(0, " + Js.Fmt(raw) + ") = " + Js.Fmt(maxPerm)
            };
        }

        public HelpParamsDto Compute(EngineParams p)
        {
            var buf = double.IsNaN(p.StrBuffer) || p.StrBuffer == 0 ? 0 : p.StrBuffer;
            var mwd = double.IsNaN(p.MixWindowDays) || p.MixWindowDays == 0 ? 45 : p.MixWindowDays;
            var dto = new HelpParamsDto
            {
                D = S(p.D), Buffer = S(buf), Mix = S(p.Mix), N = S(p.N), MixWindowDays = S(mwd),
                SampleStock = SampleStock, SampleRate = SampleRate,
                Tool1 = ComputeFor(p.T1, buf), Tool2 = ComputeFor(p.T2, buf),
                Tabs = Tabs.ToList()
            };
            dto.Placeholders = new Dictionary<string, string>
            {
                ["D"] = dto.D, ["BUF"] = dto.Buffer, ["MIX"] = dto.Mix, ["N"] = dto.N, ["MWD"] = dto.MixWindowDays,
                ["T1"] = dto.Tool1.T, ["T2"] = dto.Tool2.T, ["EFFT1"] = dto.Tool1.EffT, ["EFFT2"] = dto.Tool2.EffT,
                ["T1_CALC"] = dto.Tool1.Calc, ["T2_CALC"] = dto.Tool2.Calc, ["T1_RESULT"] = dto.Tool1.Result, ["T2_RESULT"] = dto.Tool2.Result
            };
            return dto;
        }
    }
}

using Application.DTOs.Tool1;
using Application.Services.V1.Engines.Common;
using Domain.Constants;
using Domain.Entities.Common;
using Domain.Entities.Engine;

namespace Application.Services.V1.Tool1
{
    /// <summary>
    /// GM resolution / routing for Tool 1 (ports of index.html <c>resolveGM</c>, <c>buildKey2</c>,
    /// <c>buildKey3</c>, <c>routeRequest</c>) — thin wrappers over the shared <see cref="ResolveIndex"/>.
    /// </summary>
    public static class Tool1Router
    {
        /// <summary>Port of <c>resolveGM(code)</c> as the request's <c>info</c> object; null when unknown.</summary>
        public static Tool1InfoDto? Resolve(EngineData data, string? code)
        {
            var g = ResolveIndex.ResolveGm(data, code);
            return g == null ? null : new Tool1InfoDto
            {
                Brand = g.Brand, Div = g.Div, McCd = g.McCd, McDesc = g.McDesc, Mrp = g.Mrp, Flag = g.Flag,
                Season = g.Season, ArtDesc = g.ArtDesc, VendorName = g.VendorName
            };
        }

        /// <summary>Port of <c>routeRequest(gm, colour)</c>: ("B", "—" in the request), ("A", "True Repeat" / "BOBs"), ("NEW", "New Option").</summary>
        public static (string Engine, string? OptionType) Route(EngineData data, string gm, string colour) =>
            ResolveIndex.RouteRequest(data, gm, colour);

        /// <summary>Port of <c>buildKey2(info)</c>: "Brand-MC-MRP-Flag", null when brand / MC / MRP missing.</summary>
        public static string? BuildKey2(Tool1InfoDto? info)
        {
            if (info == null || string.IsNullOrEmpty(info.Brand) || string.IsNullOrEmpty(info.McCd) || string.IsNullOrEmpty(info.Mrp)) return null;
            return info.Brand + "-" + info.McCd + "-" + info.Mrp + "-" + info.Flag;
        }

        /// <summary>Port of <c>buildKey3(info, colour)</c>.</summary>
        public static string? BuildKey3(Tool1InfoDto? info, string colour)
        {
            var k2 = BuildKey2(info);
            return k2 != null ? k2 + "-" + colour : null;
        }

        /// <summary>index.html REQUIRED_KEYS that are not uploaded.</summary>
        public static List<string> MissingRequired(EngineData data) =>
            DatasetKeys.Required.Where(k => data.Get(k) == null).ToList();
    }

    /// <summary>
    /// What the New Request form offers — ports of the Category form's <c>formGMInput</c> (line 8378),
    /// <c>formPopulateColours</c> (line 8395) and <c>populateMonths</c> (line 9280). (The older
    /// <c>onGMInput</c> / <c>populateColours</c> / <c>initMonthDropdown</c> fill the planner's legacy form,
    /// which v1.1.4 no longer shows; their only differences are a 4-character minimum, a "Description"-only
    /// colour-desc header list and 13 months with a "(Current Month)" suffix.)
    /// </summary>
    public sealed class Tool1FormOptions
    {
        public Tool1FormOptionsDto Build(EngineData data, string? gm, string today)
        {
            var dto = new Tool1FormOptionsDto { Months = Months(today), MissingFiles = Tool1Router.MissingRequired(data) };
            var code = (gm ?? "").Trim();
            dto.Gm = code;
            if (code.Length < 3)
            {
                dto.ColourPlaceholder = "— enter material first —";
                return dto;
            }
            var info = Tool1Router.Resolve(data, code);
            if (info != null)
            {
                dto.ShowStrip = true;
                dto.Info = info;
                dto.Strip = new Tool1StripDto
                {
                    Brand = Dash(info.Brand), Div = Dash(info.Div), McCd = Dash(info.McCd), McDesc = Dash(info.McDesc),
                    Mrp = info.Mrp.Length > 0 ? "₹" + info.Mrp : "—", Flag = Dash(info.Flag)
                };
            }
            dto.EngineRoute = Tool1Router.Route(data, code, "").Engine;
            PopulateColours(data, code, dto);
            return dto;
        }

        private static string Dash(string? s) => string.IsNullOrEmpty(s) ? "—" : s;

        /// <summary>Port of <c>formPopulateColours(pfx, gm)</c>.</summary>
        private static void PopulateColours(EngineData data, string gmStr, Tool1FormOptionsDto dto)
        {
            dto.ColourPlaceholder = "— select colour —";
            var colDesc = new JsObjectMap<string>();
            if (data.ColMaster != null)
                foreach (var r in data.ColMaster)
                {
                    var c = r.Col("Colour", "Color", "colour", "color");
                    var d = r.Col("Colour Desc", "Color Desc", "Description", "Desc");
                    if (c.Length > 0 && c.Trim().Length > 0) colDesc[c.Trim()] = d.Length > 0 ? d.Trim() : "";
                }
            string Label(string c) => c + (colDesc.Get(c) is { Length: > 0 } d ? " - " + d : "");
            if (data.ColMaster != null && colDesc.Count > 0)
            {
                foreach (var c in colDesc.Keys.OrderBy(k => k, StringComparer.Ordinal))
                    dto.Colours.Add(new Tool1OptionDto { Value = c, Label = Label(c) });
                dto.NewColourLabel = "+ Enter colour manually...";
                return;
            }
            var colours = new HashSet<string>(StringComparer.Ordinal);
            void AddC(SheetRow r)
            {
                var cv = r.Col("Color", "Colour", "COLOR", "COLOUR", "colour", "color");
                if (cv.Length > 0 && cv.Trim().Length > 0) colours.Add(cv.Trim());
            }
            foreach (var src in new[] { data.F2, data.Bsr })
            {
                if (src == null) continue;
                foreach (var r in src)
                    if (r.Col("Generic Material", "generic material", "GENERIC MATERIAL").Trim() == gmStr) AddC(r);
            }
            var info = Tool1Router.Resolve(data, gmStr);
            if (info != null && info.McCd.Length > 0)
            {
                var mc = Tool1JsHelpers.StripDot0(info.McCd);
                var mrp = Tool1JsHelpers.StripDot0(info.Mrp);
                foreach (var src in new[] { data.F1, data.F3 })
                {
                    if (src == null) continue;
                    foreach (var r in src)
                    {
                        var rmc = Tool1JsHelpers.StripDot0(r.Col("MC CD", "MC_CD", "mc cd"));
                        var rmrp = Tool1JsHelpers.StripDot0(r.Col("MRP", "mrp"));
                        if (rmc == mc && (mrp.Length == 0 || rmrp == mrp)) AddC(r);
                    }
                }
            }
            var keys = colours.OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (keys.Count > 0)
            {
                foreach (var c in keys) dto.Colours.Add(new Tool1OptionDto { Value = c, Label = Label(c) });
                dto.NewColourLabel = "+ Enter colour manually...";
            }
            else
            {
                dto.ColourPlaceholder = "— no colours found —";
                dto.NewColourLabel = "+ Enter colour manually";
                dto.ShowManualColour = true;
            }
        }

        /// <summary>Port of <c>populateMonths()</c>: 18 months from the current month.</summary>
        public static List<Tool1OptionDto> Months(string today)
        {
            var (y, m, _) = MonthMath.ParseIsoDate(today);
            var list = new List<Tool1OptionDto>();
            for (var i = 0; i < 18; i++)
            {
                var (yy, mm) = MonthMath.AddMonths(y, m, i);
                list.Add(new Tool1OptionDto { Value = MonthMath.Mmyyyy(yy, mm), Label = MonthMath.MonLabel(yy, mm) });
            }
            return list;
        }
    }
}

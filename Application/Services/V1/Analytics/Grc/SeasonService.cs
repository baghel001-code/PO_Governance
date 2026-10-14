using System.Text.RegularExpressions;
using Application.DTOs.Analytics.Grc;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Analytics;
using Domain.Entities.Engine;

namespace Application.Services.V1.Analytics.Grc
{
    /// <summary>Pure Season Selection rules (index.html lines 2685-2747).</summary>
    public static class SeasonLogic
    {
        public const string NoSeasonsMessage = "No Season column found in Article Master yet.";
        private static readonly Regex SeasonRe = new(@"^(SS|AW)\s*[-_]?\s*([0-9]{2,4})$", RegexOptions.Compiled);
        private static readonly Regex Ws = new(@"\s+", RegexOptions.Compiled);

        /// <summary>
        /// Port of <c>_seasonSortKey(season)</c>: SS of year Y &lt; AW of year Y &lt; SS of Y+1
        /// (SS26 = 20260, AW26 = 20265). Null when unparseable.
        /// </summary>
        public static double? SortKey(string? season)
        {
            var m = SeasonRe.Match((season ?? "").Trim().ToUpperInvariant());
            if (!m.Success) return null;
            var yr = GrcJs.ParseInt(m.Groups[2].Value);
            if (yr < 100) yr += 2000;
            return yr * 10 + (m.Groups[1].Value == "AW" ? 5 : 0);
        }

        /// <summary>Display form of a season: trimmed, upper case, no "-" / "_" / whitespace ("ss-26" → "SS26").</summary>
        public static string Normalize(string raw) =>
            Ws.Replace(raw.Trim().ToUpperInvariant().Replace("-", "").Replace("_", ""), "");

        /// <summary>Port of <c>_allSeasonsSorted()</c>: distinct parseable Article Master seasons, most recent first.</summary>
        public static List<string> AllSeasonsSorted(EngineData data)
        {
            var am = data.ArtMaster;
            if (am == null) return new();
            var seen = new List<KeyValuePair<string, double>>();
            var has = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in am)
            {
                var raw = r.Col("Season", "SEASON", "season");
                if (raw.Length == 0) continue;
                var key = SortKey(raw);
                if (key == null) continue;
                var disp = Normalize(raw);
                if (has.Add(disp)) seen.Add(new(disp, key.Value));
            }
            return GrcJs.StableSort(seen, (a, b) => b.Value - a.Value).Select(e => e.Key).ToList();
        }

        /// <summary>
        /// Port of the defaulting in <c>renderSeasonPills()</c>: keep the selected seasons that still exist;
        /// an empty group falls back to the latest (Current) / second-latest (Fallback) season. With no
        /// seasons at all the selection is left untouched.
        /// </summary>
        public static SeasonSelectionDto Resolve(List<string> seasons, IReadOnlyList<string>? current, IReadOnlyList<string>? fallback)
        {
            var cur = (current ?? Array.Empty<string>()).ToList();
            var fb = (fallback ?? Array.Empty<string>()).ToList();
            if (seasons.Count == 0)
                return new SeasonSelectionDto { Seasons = seasons, SeasonCurrent = cur, SeasonFallback = fb, Message = NoSeasonsMessage };
            var validCur = cur.Where(seasons.Contains).ToList();
            var validFb = fb.Where(seasons.Contains).ToList();
            var effCur = validCur.Count > 0 ? validCur : new List<string> { seasons[0] };
            var effFb = validFb.Count > 0 ? validFb : seasons.Count > 1 ? new List<string> { seasons[1] } : new List<string>();
            return new SeasonSelectionDto
            {
                Seasons = seasons, SeasonCurrent = effCur, SeasonFallback = effFb,
                Changed = !effCur.SequenceEqual(cur) || !effFb.SequenceEqual(fb),
            };
        }

        /// <summary>Port of <c>_toggleSeasonPill(group, season)</c> followed by the re-render's validation.</summary>
        public static SeasonSelectionDto Toggle(List<string> seasons, IReadOnlyList<string> current, IReadOnlyList<string> fallback, string group, string season)
        {
            var cur = current.ToList();
            var fb = fallback.ToList();
            var arr = group == "current" ? cur : fb;
            var next = arr.Contains(season) ? arr.Where(s => s != season).ToList() : arr.Append(season).ToList();
            if (group == "current") cur = next; else fb = next;
            var r = Resolve(seasons, cur, fb);
            r.Changed = !r.SeasonCurrent.SequenceEqual(current) || !r.SeasonFallback.SequenceEqual(fallback);
            return r;
        }
    }

    /// <summary>Season Selection service (see <see cref="ISeasonService"/>).</summary>
    public sealed class SeasonService : ISeasonService
    {
        private readonly IDatasetCache _cache;
        public SeasonService(IDatasetCache cache) => _cache = cache;

        public SeasonSelectionDto Resolve(EngineData data) =>
            data.GetOrBuild("grc|seasons|" + string.Join(",", data.Params.SeasonCurrent) + "|" + string.Join(",", data.Params.SeasonFallback),
                d => SeasonLogic.Resolve(SeasonLogic.AllSeasonsSorted(d), d.Params.SeasonCurrent, d.Params.SeasonFallback));

        public SeasonSelectionDto Toggle(EngineData data, string group, string season)
        {
            var eff = Resolve(data);
            return SeasonLogic.Toggle(eff.Seasons, eff.SeasonCurrent, eff.SeasonFallback, group == "current" ? "current" : "fallback", season);
        }

        public async Task<SeasonSelectionDto> GetCurrentAsync(CancellationToken ct = default) =>
            Resolve(await _cache.GetCurrentAsync(ct));

        public async Task<SeasonSelectionDto> ToggleAsync(string group, string season, CancellationToken ct = default) =>
            Toggle(await _cache.GetCurrentAsync(ct), group, season);
    }
}

using DecisionEngine.Common;
using DecisionEngine.Model;

namespace DecisionEngine.Grc
{
    /// <summary>Outcome of <see cref="GrcHistoricalRanker.Rank"/> (the object <c>_bsrQHistorical</c> returned).</summary>
    public sealed class GrcHistResult
    {
        public bool FuturePeriod { get; init; }
        public double WeeksElapsed { get; init; }
        public bool InsufficientPool { get; init; }
        public int PoolSize { get; init; }
        public double PeerMin { get; init; }
        public bool TooNew { get; init; }
        public double EstimatedWof { get; init; }
        public double WofNow { get; init; }
        public double WofGate { get; init; }
        public double Wof { get; init; }
        public double Pdstr { get; init; }
        public string Q { get; init; } = "";
    }

    /// <summary>
    /// Port of index.html <c>_bsrQHistorical</c> (line 2833) with <c>quartile</c> / <c>getPDSTR</c>
    /// (lines 4216-4240): ranks an article against the CY peer pool AS IT EXISTED at a past period —
    /// only peers whose own back-calculated WOF had cleared the gate by then; fallback seasons when the
    /// CY pool is thinner than the peer minimum. One instance = one browser run (the run-scoped
    /// <c>_bsrPoolCache</c>).
    /// </summary>
    public sealed class GrcHistoricalRanker
    {
        private sealed class BsrRow
        {
            public required SheetRow Row;
            public required string Art;   // _n(Article Type || BOGO/NON-BOGO || Flag)
            public required double WofNow; // _sf(col(WEEK ON FLOOR, WOF))
            public required double PoolPdstr; // getPDSTR(r)
        }

        private readonly IReadOnlyList<SheetRow> _all;
        private readonly Dictionary<string, GrcResolveInfo> _resolve;
        private readonly HashSet<string> _curSet, _fbSet;
        private readonly double _wofGate, _peerMin, _asOfMs;
        private readonly string _periodType;

        private Dictionary<string, List<BsrRow>>? _bucketCur, _bucketFb;
        private Dictionary<string, SheetRow>? _cyIndex;
        private readonly Dictionary<string, List<BsrRow>> _poolCache = new(StringComparer.Ordinal);

        public GrcHistoricalRanker(EngineData data, IEnumerable<string> seasonCurrent, IEnumerable<string> seasonFallback,
                                   double wofGate, double peerMin, string periodType, double asOfMs)
        {
            _all = data.Bsr ?? Array.Empty<SheetRow>();
            _resolve = GrcLookups.ResolveMap(data);
            _curSet = new HashSet<string>(seasonCurrent, StringComparer.Ordinal);
            _fbSet = new HashSet<string>(seasonFallback, StringComparer.Ordinal);
            _wofGate = wofGate;
            _peerMin = peerMin;
            _periodType = periodType;
            _asOfMs = asOfMs;
        }

        private string? SeasonOf(SheetRow r)
        {
            var g = Js.ToJsString(GrcJs.Or(r, "Generic Material", "GENERIC MATERIAL")).Trim();
            if (!_resolve.TryGetValue(g, out var info) || info.Season.Length == 0) return null;
            return SeasonLogic.Normalize(info.Season);
        }

        private static double GetPdstr(SheetRow r)
        {
            var raw = r.Col("AVG PERDAY SELL THRU%", "AVG PERDAY SELL THRU %", "avg perday sell thru%");
            var v = Js.Num(raw);
            if (raw.Contains('%')) v /= 100;
            return v;
        }

        private void BuildBuckets()
        {
            _bucketCur = new(StringComparer.Ordinal);
            _bucketFb = new(StringComparer.Ordinal);
            foreach (var r in _all)
            {
                var bKey = Js.N(GrcJs.Or(r, "MC CD", "MC_CD")) + "|" + Js.N(GrcJs.Or(r, "MRP"));
                var s = SeasonOf(r);
                if (s == null) continue;
                var inCur = _curSet.Contains(s);
                var inFb = _fbSet.Contains(s);
                if (!inCur && !inFb) continue;
                var br = new BsrRow
                {
                    Row = r,
                    Art = Js.N(GrcJs.Or(r, "Article Type", "BOGO/NON-BOGO", "Flag")),
                    WofNow = Js.Sf(r.Col("WEEK ON FLOOR", "WOF")),
                    PoolPdstr = GetPdstr(r),
                };
                if (inCur) Add(_bucketCur, bKey, br);
                if (inFb) Add(_bucketFb, bKey, br);
            }

            static void Add(Dictionary<string, List<BsrRow>> d, string k, BsrRow r)
            {
                if (!d.TryGetValue(k, out var l)) d[k] = l = new();
                l.Add(r);
            }
        }

        /// <summary>
        /// Ranks GM × colour of bucket MC × MRP × flag at <paramref name="period"/>. Null = the GM + colour
        /// is not in the BSR at all (→ "No Peer Data").
        /// </summary>
        public GrcHistResult? Rank(string gm, object? colour, string mc, string mrp, string flag, string period)
        {
            var weeksElapsed = GrcPeriodMath.WeeksElapsedSincePeriod(period, _periodType, _asOfMs);
            // Period after the "as of" date → excluded (negative elapsed time would inflate the WOF).
            if (weeksElapsed < 0) return new GrcHistResult { FuturePeriod = true, WeeksElapsed = weeksElapsed };

            var nMc = Js.N(mc);
            var nMrp = Js.N(mrp);
            var nFlag = Js.N(flag);
            if (_bucketCur == null) BuildBuckets();

            List<BsrRow> FilterByWof(List<BsrRow>? rows)
            {
                var res = new List<BsrRow>();
                if (rows == null) return res;
                foreach (var r in rows)
                {
                    if (!(nFlag.Length == 0 || r.Art == nFlag || r.Art.Length == 0)) continue;
                    if (r.WofNow - weeksElapsed >= _wofGate) res.Add(r);
                }
                return res;
            }

            var bucketKey = nMc + "|" + nMrp;
            var poolKey = nMc + "|" + nMrp + "|" + nFlag + "|" + period;
            if (!_poolCache.TryGetValue(poolKey, out var pool))
            {
                pool = FilterByWof(_bucketCur!.GetValueOrDefault(bucketKey));
                if (pool.Count < _peerMin)
                {
                    var fbPool = FilterByWof(_bucketFb!.GetValueOrDefault(bucketKey));
                    if (fbPool.Count > 0) pool = fbPool;
                }
                _poolCache[poolKey] = pool;
            }
            if (pool.Count < _peerMin) return new GrcHistResult { InsufficientPool = true, PoolSize = pool.Count, PeerMin = _peerMin };

            _cyIndex ??= BuildCyIndex();
            if (!_cyIndex.TryGetValue(Js.N(gm) + "|" + Js.N(colour), out var row)) return null;

            var wofNow = Js.Sf(row.Col("WEEK ON FLOOR", "WOF"));
            var estimatedWof = wofNow - weeksElapsed;
            if (estimatedWof < _wofGate) return new GrcHistResult { TooNew = true, EstimatedWof = estimatedWof, WofNow = wofNow, WofGate = _wofGate };

            var pdstrRaw = row.Col("AVG PERDAY SELL THRU%", "AVG PERDAY SELL THRU %");
            if (pdstrRaw.Length == 0) pdstrRaw = "0";
            var pdstr = Js.Sf(pdstrRaw);
            if (pdstrRaw.Contains('%')) pdstr /= 100;
            return new GrcHistResult { Wof = wofNow, Pdstr = pdstr, Q = Quartile(pdstr, pool), PoolSize = pool.Count };
        }

        private Dictionary<string, SheetRow> BuildCyIndex()
        {
            var idx = new Dictionary<string, SheetRow>(StringComparer.Ordinal);
            foreach (var r in _all)
            {
                var key = Js.N(GrcJs.Or(r, "Generic Material", "GENERIC MATERIAL")) + "|" + Js.N(GrcJs.Or(r, "Color", "Colour", "COLOR"));
                idx.TryAdd(key, r);
            }
            return idx;
        }

        /// <summary>
        /// Port of <c>quartile(val, pool)</c>: percentile rank of <paramref name="val"/> in the pool's PDSTRs;
        /// top 25 % = Q1 … bottom 25 % = Q4 (management convention).
        /// </summary>
        private static string Quartile(double val, List<BsrRow> pool)
        {
            if (pool.Count == 0) return "Q3";
            var le = pool.Count(r => r.PoolPdstr <= val);
            var pct = (double)le / pool.Count * 100;
            if (pct <= 25) return "Q4";
            if (pct <= 50) return "Q3";
            if (pct <= 75) return "Q2";
            return "Q1";
        }
    }
}

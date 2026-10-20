namespace DecisionEngine.Results.Analytics.Grc
{
    /// <summary>
    /// Raw result of the GRC back-test (port of index.html <c>_computeGRCBacktest</c>, line 2950):
    /// every aggregation the screen is built from. Keys of the per-period maps are display periods
    /// ("WW.YYYY" weeks, or "MM.YYYY" months when rolled up); combined keys use "|||"
    /// (<c>period|||division</c>), exactly as the browser did.
    /// </summary>
    public sealed class GrcBacktestResultDto
    {
        /// <summary>Display periods present in the GRC file (all rows, unfiltered), chronological.</summary>
        public List<string> MonthsPresent { get; set; } = new();
        public Dictionary<string, GrcAggDto> PerMonth { get; set; } = new();
        public Dictionary<string, GrcAggDto> PerDivision { get; set; } = new();
        public Dictionary<string, GrcAggDto> PerCatHead { get; set; } = new();
        public Dictionary<string, GrcAggDto> PerBrand { get; set; } = new();
        public Dictionary<string, GrcAggDto> PerMonthDivision { get; set; } = new();
        public Dictionary<string, GrcAggDto> PerMonthCatHead { get; set; } = new();
        /// <summary>"week" or "month" — "month" when weekly data is rolled up (View by: Month).</summary>
        public string PeriodType { get; set; } = "month";

        /// <summary>Total GRC qty (Single + Multi Concept, every resolvable row) per group.</summary>
        public Dictionary<string, double> TrueTotalByMonth { get; set; } = new();
        public Dictionary<string, double> TrueTotalByDivision { get; set; } = new();
        public Dictionary<string, double> TrueTotalByCatHead { get; set; } = new();
        public Dictionary<string, double> TrueTotalByBrand { get; set; } = new();
        public Dictionary<string, double> TrueTotalByMonthDivision { get; set; } = new();
        public Dictionary<string, double> TrueTotalByMonthCatHead { get; set; } = new();

        /// <summary>Filter universes (collected before the filters are applied).</summary>
        public List<string> AllDivisions { get; set; } = new();
        /// <summary>Alphabetical, "Unassigned" last.</summary>
        public List<string> AllCatHeads { get; set; } = new();
        public List<string> AllBrands { get; set; } = new();

        /// <summary>Multi Concept MRP rows only, one per GRC line, with the bucket it landed in.</summary>
        public List<GrcFlatRowDto> FlatRows { get; set; } = new();
    }

    /// <summary>Per-group aggregate (<c>aggregateBy</c>): qty per bucket, Multi Concept total, judgeable qty, value.</summary>
    public sealed class GrcAggDto
    {
        /// <summary>Qty per bucket — every bucket of <see cref="GrcBuckets.All"/> is present.</summary>
        public Dictionary<string, double> Buckets { get; set; } = new();
        /// <summary>Multi Concept GRC qty.</summary>
        public double Total { get; set; }
        public double Judgeable { get; set; }
        public double Value { get; set; }
        /// <summary>Value of judgeable Q4 + Abs. Block rows.</summary>
        public double WrongValue { get; set; }

        public double Bucket(string b) => Buckets.TryGetValue(b, out var v) ? v : 0;
    }

    public sealed class GrcFlatRowDto
    {
        public string Period { get; set; } = "";
        public string DisplayPeriod { get; set; } = "";
        public string Division { get; set; } = "";
        public string CatHead { get; set; } = "";
        public string Brand { get; set; } = "";
        public string Bucket { get; set; } = "";
        public double Qty { get; set; }
        public double Mrp { get; set; }
        public double Value { get; set; }
    }

    /// <summary>Bucket names and colours (index.html GRC_BUCKET_ORDER / GRC_BUCKET_COLOR).</summary>
    public static class GrcBuckets
    {
        public const string Q1 = "Q1", Q2 = "Q2", Q3 = "Q3", Q4 = "Q4", AbsBlock = "Abs. Block";
        public const string NoPeerData = "No Peer Data";
        public const string TooNew = "Too New At The Time";
        public const string InsufficientPool = "Insufficient Historical Peer Pool";
        public const string FuturePeriod = "Future Period (After As-Of Date)";
        public const string SingleConcept = "Single Concept — out of scope";
        public const string NotResolved = "Not Resolved (no Article Master / UM / F2 match)";

        /// <summary>The five ranked buckets, in display order.</summary>
        public static readonly string[] Order = { Q1, Q2, Q3, Q4, AbsBlock };
        /// <summary>Every bucket an aggregate carries (ALL_BUCKETS).</summary>
        public static readonly string[] All = { Q1, Q2, Q3, Q4, AbsBlock, NoPeerData, TooNew, InsufficientPool, FuturePeriod };
        /// <summary>Buckets excluded from "Judgeable".</summary>
        public static readonly string[] NonJudgeable = { NoPeerData, TooNew, InsufficientPool, FuturePeriod };

        public static readonly IReadOnlyDictionary<string, string> Colors = new Dictionary<string, string>
        {
            [Q1] = "#2DD9C4", [Q2] = "#6FE0D0", [Q3] = "#F2B84B", [Q4] = "#FF6B5E", [AbsBlock] = "#D8483C",
            [TooNew] = "#B8C4D9", [InsufficientPool] = "#D9C7EE", [FuturePeriod] = "#F5C6E0",
        };

        /// <summary>Article Trace colours (GRC_TRACE_BUCKET_COLOR) — <see cref="Colors"/> + Single Concept grey.</summary>
        public static readonly IReadOnlyDictionary<string, string> TraceColors =
            Colors.Concat(new[] { new KeyValuePair<string, string>(SingleConcept, "#9CA3AF") }).ToDictionary(k => k.Key, k => k.Value);

        /// <summary>Fallback trace colour for buckets without one (e.g. No Peer Data).</summary>
        public const string DefaultTraceColor = "#5B6B85";
    }
}

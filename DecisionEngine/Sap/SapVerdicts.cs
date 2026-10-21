namespace DecisionEngine.Sap
{
    /// <summary>
    /// SAP verdict values of the /decisions contract (spec v1.4 §6, po-governance-sap-api.yaml DecisionResponse):
    /// ALLOW / REDUCE / WATCH / HOLD with the one-character codes A / R / W / H. <see cref="Block"/> (code B) is the
    /// owner-approved future value (handover §8 item 2); it is only sent when <c>SapInterface:BlockVerdict = BLOCK</c>.
    /// </summary>
    public static class SapVerdicts
    {
        public const string Allow = "ALLOW";
        public const string Reduce = "REDUCE";
        public const string Watch = "WATCH";
        public const string Hold = "HOLD";
        /// <summary>Not in spec v1.4 (breaking change when switched on): only with BlockVerdict = BLOCK.</summary>
        public const string Block = "BLOCK";

        /// <summary>The verdicts of spec v1.4, least severe first.</summary>
        public static readonly string[] SpecValues = { Allow, Reduce, Watch, Hold };

        /// <summary>A / R / W / H (B for BLOCK).</summary>
        public static string Code(string verdict) => verdict switch
        {
            Allow => "A",
            Reduce => "R",
            Watch => "W",
            Hold => "H",
            Block => "B",
            _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Unknown SAP verdict")
        };

        /// <summary>Spec: "the most severe item verdict (HOLD &gt; WATCH &gt; REDUCE &gt; ALLOW)"; BLOCK above HOLD.</summary>
        public static int Severity(string verdict) => verdict switch
        {
            Allow => 0,
            Reduce => 1,
            Watch => 2,
            Hold => 3,
            Block => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Unknown SAP verdict")
        };

        /// <summary>The most severe of <paramref name="verdicts"/> (ALLOW when empty).</summary>
        public static string MostSevere(IEnumerable<string> verdicts)
        {
            var best = Allow;
            foreach (var v in verdicts)
                if (Severity(v) > Severity(best)) best = v;
            return best;
        }
    }

    /// <summary>route_to values (spec §6 "Routing in LIVE mode").</summary>
    public static class SapRoutes
    {
        public const string None = "NONE";
        public const string Buyer = "BUYER";
        public const string CatHead = "CATHEAD";
        public const string BmHead = "BMHEAD";

        /// <summary>
        /// ALLOW → NONE, REDUCE → BUYER, WATCH → CATHEAD, HOLD (and BLOCK) → CATHEAD, or BMHEAD when the B&amp;M head is
        /// required as well (spec: "HOLD → CATHEAD or BMHEAD — B&amp;M head as well when bm_head_required = true").
        /// </summary>
        public static string For(string overallVerdict, bool bmHeadRequired) => overallVerdict switch
        {
            SapVerdicts.Allow => None,
            SapVerdicts.Reduce => Buyer,
            SapVerdicts.Watch => CatHead,
            SapVerdicts.Hold or SapVerdicts.Block => bmHeadRequired ? BmHead : CatHead,
            _ => throw new ArgumentOutOfRangeException(nameof(overallVerdict), overallVerdict, "Unknown SAP verdict")
        };
    }
}

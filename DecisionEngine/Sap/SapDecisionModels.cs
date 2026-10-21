namespace DecisionEngine.Sap
{
    /// <summary>One PO item as the SAP decision engine needs it (mapped from the /decisions request by the Application layer).</summary>
    public sealed class SapItemInput
    {
        public int PoItem { get; set; }
        public string Article { get; set; } = "";
        public string? GenericArticle { get; set; }
        public string? Colour { get; set; }
        public double OrderQty { get; set; }
        public string? Uom { get; set; }
        /// <summary>YYYY-MM-DD (first schedule line).</summary>
        public string? DeliveryDate { get; set; }
        public double? NetValue { get; set; }
    }

    /// <summary>Behaviour switches (SapInterface configuration) the pure engine needs.</summary>
    public sealed class SapDecisionOptions
    {
        /// <summary>"HOLD" (default, spec v1.4) or "BLOCK": the verdict sent for the tool's Block decisions.</summary>
        public string BlockVerdict { get; set; } = SapVerdicts.Hold;

        /// <summary>
        /// What happens when some (not all) items cannot be decided (unknown article, no colour, no delivery date):
        /// "REJECT" (default) = the whole request is answered 422 (SAP: NOT_CHECKED); "WATCH" = those items get
        /// WATCH / CANNOT_VALIDATE and the others are decided. When no item can be decided the answer is always 422.
        /// </summary>
        public string UnknownItemPolicy { get; set; } = "REJECT";

        /// <summary>Rule hook for bm_head_required (pending business rule, handover §8 item 5).</summary>
        public SapBmHeadRule BmHeadRule { get; set; } = new();
    }

    /// <summary>
    /// The B&amp;M head escalation rule (spec v1.4 §6 bm_head_required; "above a set limit, also B&amp;M head" in the Developer
    /// Brief §8). The limit is not decided yet (handover §8 item 5): off by default (always false). When enabled, the
    /// B&amp;M head is required for a HOLD / BLOCK (and WATCH when <see cref="IncludeWatch"/>) whose PO net value is at least
    /// <see cref="MinNetValue"/>.
    /// </summary>
    public sealed class SapBmHeadRule
    {
        public bool Enabled { get; set; }
        public double MinNetValue { get; set; }
        public bool IncludeWatch { get; set; }

        public bool Evaluate(string overallVerdict, double poNetValue)
        {
            if (!Enabled) return false;
            var severe = overallVerdict is SapVerdicts.Hold or SapVerdicts.Block || (IncludeWatch && overallVerdict == SapVerdicts.Watch);
            return severe && poNetValue >= MinNetValue;
        }
    }

    /// <summary>The figures behind an item verdict (spec DecisionResponse items[].metrics); null = not available for this engine path.</summary>
    public sealed class SapItemMetrics
    {
        /// <summary>Colour plan of the month after delivery (Single Concept chain, plan × colour share).</summary>
        public double? PlanQty { get; set; }
        public double? StockQty { get; set; }
        /// <summary>Pending PO (F2) + today's approved Tool 1 quantities for the colour, all months.</summary>
        public double? OpenPoQty { get; set; }
        /// <summary>Max Permissible Qty.</summary>
        public double? HeadroomQty { get; set; }
        /// <summary>Days of cover (STR) from Day 1 of the month after delivery, including this PO.</summary>
        public double? CoverDays { get; set; }
        /// <summary>Average per-day sell-thru % (BSR) — Multi Concept.</summary>
        public double? SellThroughPct { get; set; }
    }

    /// <summary>The decision of one PO item.</summary>
    public sealed class SapItemDecision
    {
        public int PoItem { get; set; }
        public string Article { get; set; } = "";
        public string? GenericArticle { get; set; }
        public string? ColourSent { get; set; }
        public double OrderQty { get; set; }

        /// <summary>The tool's GM / colour / delivery month the item was decided on (null when not resolved).</summary>
        public string? Gm { get; set; }
        public string GmMatch { get; set; } = "";
        public string? Colour { get; set; }
        public string ColourMatch { get; set; } = "";
        public string? Month { get; set; }
        /// <summary>Total quantity of the PO's items with the same GM + colour + month (decided together).</summary>
        public double GroupQty { get; set; }

        public bool Decided { get; set; }
        /// <summary>Why the item cannot be decided (null when decided).</summary>
        public string? Problem { get; set; }

        public string Verdict { get; set; } = SapVerdicts.Allow;
        public string VerdictCode { get; set; } = "A";
        public double? MaxQty { get; set; }
        public string ReasonCode { get; set; } = "";
        public string ReasonText { get; set; } = "";
        public string RuleId { get; set; } = "";
        public SapOutcomePath? Path { get; set; }
        public SapItemMetrics Metrics { get; set; } = new();

        /// <summary>Tool 1 engine ("B", "A", "NEW"), its decision label and full reason text (for the decision log / portal).</summary>
        public string Engine { get; set; } = "";
        public string? OptionType { get; set; }
        public string EngineDecision { get; set; } = "";
        public string EngineReason { get; set; } = "";
    }

    /// <summary>The decision for a whole PO.</summary>
    public sealed class SapDecisionResult
    {
        public List<SapItemDecision> Items { get; set; } = new();
        /// <summary>Data files the engines need but that are not uploaded (then nothing is decided).</summary>
        public List<string> MissingFiles { get; set; } = new();
        /// <summary>True when the request must be answered 422 (see <see cref="SapDecisionOptions.UnknownItemPolicy"/>).</summary>
        public bool Rejected { get; set; }
        public string? RejectMessage { get; set; }
        public string OverallVerdict { get; set; } = SapVerdicts.Allow;
        public string VerdictCode { get; set; } = "A";
        public string RouteTo { get; set; } = SapRoutes.None;
        public bool BmHeadRequired { get; set; }
        public string SummaryText { get; set; } = "";
    }
}

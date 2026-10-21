using DecisionEngine.Model;
using DecisionEngine.Tool1;

namespace DecisionEngine.Sap
{
    /// <summary>Which decision path of the Tool 1 engines produced an outcome (one row of <see cref="SapVerdictMap.Table"/>).</summary>
    public enum SapOutcomePath
    {
        // Single Concept (runEngineB)
        CoverWithinNorm,
        NoPlanData,
        CoverAboveNorm,
        CoverNoHeadroom,
        MixBelowMin,
        // Multi Concept (runEngineA) — True Repeat / BOBs fallthrough peer ranking
        PeerQ1,
        PeerQ2,
        PeerQ3WithinBorder,
        PeerQ3BeyondBorder,
        PeerQ4Softened,
        BlockQ4,
        BlockAbsolute,
        // Multi Concept — BOBs siblings
        BobsSiblingsOk,
        BobsSiblingLow,
        LowHistory,
        NoPeerPool,
        // NEW article
        NewArticle,
        // Engine could not decide (GM not resolved, invalid month) / item not decidable for SAP
        CannotValidate
    }

    /// <summary>One row of the tool → SAP mapping: the engine decision it applies to, the SAP verdict, reason code and rule id.</summary>
    /// <param name="Verdict">The SAP verdict. <see cref="SapVerdicts.Hold"/> on the Block rows means "HOLD or BLOCK by BlockVerdict".</param>
    public sealed record SapVerdictRule(SapOutcomePath Path, string EngineDecision, string Verdict, string ReasonCode, string RuleId, string ReasonCodeText, bool IsBlock = false);

    /// <summary>
    /// <b>The</b> mapping of the tool's Tool 1 decisions onto the SAP /decisions verdicts (handover §8 item 2, owner-approved):
    /// <list type="bullet">
    /// <item>Auto Allow / Allow → ALLOW</item>
    /// <item>Single Concept quantity above Max Permissible with Max Permissible &gt; 0 → REDUCE + max_qty</item>
    /// <item>Max Permissible 0 (Hold — Don't Raise) → HOLD</item>
    /// <item>Watch List → WATCH (route CATHEAD)</item>
    /// <item>Block → HOLD with reason_code BLOCK — or the new verdict BLOCK when <c>SapInterface:BlockVerdict = BLOCK</c>
    /// (one switch, see <see cref="VerdictFor"/>)</item>
    /// <item>Cannot Validate / unknown article → not decidable: 422 for the request, or WATCH CANNOT_VALIDATE per item
    /// when <c>SapInterface:UnknownItemPolicy = WATCH</c></item>
    /// </list>
    /// Pure: classifies a <see cref="Tool1Outcome"/> by the engine's own fields (engine, decision, quartile, Max Permissible,
    /// the v1.1.4 reason texts that are fixed by the parity tests). The same reason codes are seeded into SAP_REASON_CODES
    /// (database/sqlserver/05_sap_interface.sql, Code_Type VERDICT).
    /// </summary>
    public static class SapVerdictMap
    {
        public const string BlockReasonCode = "BLOCK";

        /// <summary>The mapping table (one row per engine decision path).</summary>
        public static readonly IReadOnlyList<SapVerdictRule> Table = new[]
        {
            new SapVerdictRule(SapOutcomePath.CoverWithinNorm, Decisions.AutoAllow, SapVerdicts.Allow, "COVER_WITHIN_NORM", "COVER-00", "Cover within the norm (STR <= T + buffer)"),
            new SapVerdictRule(SapOutcomePath.NoPlanData, Decisions.AutoAllow, SapVerdicts.Allow, "NO_PLAN_DATA", "PLAN-00", "No plan for the MC-MRP-Flag: cover not computed, allowed"),
            new SapVerdictRule(SapOutcomePath.CoverAboveNorm, Decisions.Hold, SapVerdicts.Reduce, "COVER_ABOVE_NORM", "COVER-01", "Cover above the norm: reduce to Max Permissible"),
            new SapVerdictRule(SapOutcomePath.CoverNoHeadroom, Decisions.Hold, SapVerdicts.Hold, "COVER_NO_HEADROOM", "COVER-02", "Existing stock + pending already above the norm: Max Permissible 0"),
            new SapVerdictRule(SapOutcomePath.MixBelowMin, Decisions.WatchList, SapVerdicts.Watch, "MIX_BELOW_MIN", "MIX-01", "Colour mix below the minimum share of its MC-MRP"),
            new SapVerdictRule(SapOutcomePath.PeerQ1, Decisions.Allow, SapVerdicts.Allow, "PEER_Q1", "PEER-Q1", "Ranks Q1 in its BSR peer group"),
            new SapVerdictRule(SapOutcomePath.PeerQ2, Decisions.Allow, SapVerdicts.Allow, "PEER_Q2", "PEER-Q2", "Ranks Q2 in its BSR peer group"),
            new SapVerdictRule(SapOutcomePath.PeerQ3WithinBorder, Decisions.Allow, SapVerdicts.Allow, "PEER_Q3", "PEER-Q3-OK", "Ranks Q3, at or near the peer average"),
            new SapVerdictRule(SapOutcomePath.PeerQ3BeyondBorder, Decisions.WatchList, SapVerdicts.Watch, "PEER_Q3", "PEER-Q3-GAP", "Ranks Q3, below the peer average beyond the border zone"),
            new SapVerdictRule(SapOutcomePath.PeerQ4Softened, Decisions.WatchList, SapVerdicts.Watch, "PEER_Q4", "PEER-Q4-WATCH", "Ranks Q4 but at or near the peer average"),
            new SapVerdictRule(SapOutcomePath.BlockQ4, Decisions.Block, SapVerdicts.Hold, BlockReasonCode, "BLOCK-Q4", "Block: ranks Q4, confirmed slow seller", IsBlock: true),
            new SapVerdictRule(SapOutcomePath.BlockAbsolute, Decisions.Block, SapVerdicts.Hold, BlockReasonCode, "BLOCK-ABS", "Block: sell-thru below 1 %/day after 4+ weeks on floor", IsBlock: true),
            new SapVerdictRule(SapOutcomePath.BobsSiblingsOk, Decisions.Allow, SapVerdicts.Allow, "BOBS_SIBLINGS_OK", "BOBS-01", "New colour: all sibling colours rank Q1/Q2"),
            new SapVerdictRule(SapOutcomePath.BobsSiblingLow, Decisions.WatchList, SapVerdicts.Watch, "BOBS_SIBLING_LOW", "BOBS-02", "New colour: a sibling colour ranks Q3/Q4"),
            new SapVerdictRule(SapOutcomePath.LowHistory, Decisions.Allow, SapVerdicts.Allow, "LOW_HISTORY", "WOF-00", "Too little selling history (weeks on floor below the gate), allowed"),
            new SapVerdictRule(SapOutcomePath.NoPeerPool, Decisions.Allow, SapVerdicts.Allow, "NO_PEER_POOL", "PEER-00", "No BSR peer group available, allowed"),
            new SapVerdictRule(SapOutcomePath.NewArticle, Decisions.AutoAllow, SapVerdicts.Allow, "NEW_ARTICLE", "NEW-00", "New article: not in Universe Master or BSR"),
            new SapVerdictRule(SapOutcomePath.CannotValidate, Decisions.CannotValidate, SapVerdicts.Watch, "CANNOT_VALIDATE", "CV-00", "The tool cannot validate this item (unknown article / missing data)"),
        };

        private static readonly Dictionary<SapOutcomePath, SapVerdictRule> ByPath = Table.ToDictionary(r => r.Path);

        public static SapVerdictRule Rule(SapOutcomePath path) => ByPath[path];

        /// <summary>
        /// The SAP verdict of a rule. Block rows answer HOLD (default, spec v1.4) or BLOCK when
        /// <paramref name="blockVerdict"/> is "BLOCK" (SapInterface:BlockVerdict); everything else as in the table.
        /// </summary>
        public static string VerdictFor(SapVerdictRule rule, string? blockVerdict) =>
            rule.IsBlock && string.Equals((blockVerdict ?? "").Trim(), SapVerdicts.Block, StringComparison.OrdinalIgnoreCase)
                ? SapVerdicts.Block
                : rule.Verdict;

        /// <summary>The engine path of a Tool 1 outcome (the first Single Concept run for engine B).</summary>
        public static SapOutcomePath Classify(Tool1Outcome o)
        {
            if (o.Engine == "NEW") return SapOutcomePath.NewArticle;
            if (o.Engine == "B")
            {
                var b = o.BFirst ?? o.B;
                var d = b?.Decision ?? o.Decision;
                if (d == Decisions.CannotValidate || b == null) return SapOutcomePath.CannotValidate;
                if (d == Decisions.WatchList) return SapOutcomePath.MixBelowMin;
                if (d == Decisions.Hold || d == Decisions.ReduceRecommended)
                    return b.MaxPermQty is double m && double.IsFinite(m) && m > 0 ? SapOutcomePath.CoverAboveNorm : SapOutcomePath.CoverNoHeadroom;
                // Auto Allow: no plan for the bucket (windowDemand 0) or cover within the norm
                if (b.WindowDemand is 0 || b.DecisionReason.StartsWith("No forecast data found", StringComparison.Ordinal))
                    return SapOutcomePath.NoPlanData;
                return SapOutcomePath.CoverWithinNorm;
            }
            if (o.Engine == "A")
            {
                var a = o.A;
                var d = a?.Decision ?? o.Decision;
                var reason = a?.DecisionReason ?? o.DecisionReason ?? "";
                var q = a?.Quartile;
                if (d == Decisions.Block)
                    return reason.StartsWith("Absolute Block override", StringComparison.Ordinal) ? SapOutcomePath.BlockAbsolute : SapOutcomePath.BlockQ4;
                if (d == Decisions.WatchList)
                {
                    if (reason.StartsWith("BOBs: ", StringComparison.Ordinal) && reason.Contains("sibling colourway", StringComparison.Ordinal))
                        return SapOutcomePath.BobsSiblingLow;
                    return q == "Q3" ? SapOutcomePath.PeerQ3BeyondBorder : SapOutcomePath.PeerQ4Softened;
                }
                if (d == Decisions.Allow || d == Decisions.AutoAllow)
                {
                    if (reason.StartsWith("BOBs: All", StringComparison.Ordinal)) return SapOutcomePath.BobsSiblingsOk;
                    if (reason.Contains("Insufficient", StringComparison.Ordinal) || reason.StartsWith("BOBs: No sibling", StringComparison.Ordinal))
                        return SapOutcomePath.LowHistory;
                    if (reason.Contains("peer pool", StringComparison.OrdinalIgnoreCase)) return SapOutcomePath.NoPeerPool;
                    return q switch
                    {
                        "Q1" => SapOutcomePath.PeerQ1,
                        "Q2" => SapOutcomePath.PeerQ2,
                        "Q3" or "Q4" => SapOutcomePath.PeerQ3WithinBorder,
                        _ => SapOutcomePath.NoPeerPool
                    };
                }
                return SapOutcomePath.CannotValidate;
            }
            return SapOutcomePath.CannotValidate;
        }
    }
}

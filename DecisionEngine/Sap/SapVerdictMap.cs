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
        // Multi Concept (Tool1EngineA) — True Repeat peer ranking, Step 3 bands: top 20% allowed (PeerQ1 = the Allow band),
        // 20–30% exception to the B&M Head, below 30% blocked (BlockBelowQ1 = below the top 30%)
        PeerQ1,
        PeerException,
        BlockBelowQ1,
        BlockAbsolute,
        // Multi Concept rule Step 2 — the Entry / Mid / Fashion minimum of a Q1 (BOBs: of the oldest other colour)
        BlockBelowEmfMinimum,
        EmfNotMaintained,
        // Multi Concept — BOBs: every other colour must rank Q1
        BobsSiblingsOk,
        BlockBobsSibling,
        LowHistory,
        NoPeerPool,
        // NEW article
        NewArticle,
        // Engine could not decide (GM not resolved, invalid month) / item not decidable for SAP
        CannotValidate
    }

    /// <summary>One row of the tool → SAP mapping: the engine decision it applies to, the SAP verdict, reason code and rule id.</summary>
    /// <param name="Verdict">The SAP verdict. <see cref="SapVerdicts.Block"/> on the Block rows (HOLD with SapInterface:BlockVerdict = HOLD).</param>
    public sealed record SapVerdictRule(SapOutcomePath Path, string EngineDecision, string Verdict, string ReasonCode, string RuleId, string ReasonCodeText, bool IsBlock = false);

    /// <summary>
    /// <b>The</b> mapping of the tool's Tool 1 decisions onto the SAP /decisions verdicts (handover §8 item 2, owner-approved):
    /// <list type="bullet">
    /// <item>Auto Allow / Allow → ALLOW</item>
    /// <item>Single Concept quantity above Max Permissible with Max Permissible &gt; 0 → REDUCE + max_qty</item>
    /// <item>Max Permissible 0 (Hold — Don't Raise) → HOLD; v1.1.6 "No Plan Available" Hold (no F4 plan in the requirement
    /// window) → HOLD with reason NO_PLAN_DATA</item>
    /// <item>Watch List → WATCH (route CATHEAD) — single concept only (multi concept rule Step 1: no Watch List)</item>
    /// <item>Block → BLOCK (code B) with reason_code BLOCK — or HOLD with reason_code BLOCK when
    /// <c>SapInterface:BlockVerdict = HOLD</c> (spec v1.4 without BLOCK; one switch, see <see cref="VerdictFor"/>)</item>
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
            // v1.1.6 D1: no F4 plan in the requirement window → Hold "No Plan Available" (was Auto Allow / ALLOW)
            new SapVerdictRule(SapOutcomePath.NoPlanData, Decisions.Hold, SapVerdicts.Hold, "NO_PLAN_DATA", "PLAN-00", "No Plan Available: no F4 plan for the MC-MRP-Flag in the requirement window, ask the planning team to add it"),
            new SapVerdictRule(SapOutcomePath.CoverAboveNorm, Decisions.Hold, SapVerdicts.Reduce, "COVER_ABOVE_NORM", "COVER-01", "Cover above the norm: reduce to Max Permissible"),
            new SapVerdictRule(SapOutcomePath.CoverNoHeadroom, Decisions.Hold, SapVerdicts.Hold, "COVER_NO_HEADROOM", "COVER-02", "Existing stock + pending already above the norm: Max Permissible 0"),
            new SapVerdictRule(SapOutcomePath.MixBelowMin, Decisions.WatchList, SapVerdicts.Watch, "MIX_BELOW_MIN", "MIX-01", "Colour mix below the minimum share of its MC-MRP"),
            new SapVerdictRule(SapOutcomePath.PeerQ1, Decisions.Allow, SapVerdicts.Allow, "PEER_Q1", "PEER-Q1", "In the top 20 % of its BSR peer group (same Brand, MC, MRP, Flag) and meets its Entry / Mid / Fashion minimum"),
            new SapVerdictRule(SapOutcomePath.PeerException, Decisions.Exception, SapVerdicts.Hold, "PEER_EXCEPTION", "EXC-L8", "Exception band (20-30 % of its BSR peer group): needs the B&M Head's approval (MRA Level 8)"),
            new SapVerdictRule(SapOutcomePath.BlockBelowQ1, Decisions.Block, SapVerdicts.Block, BlockReasonCode, "BLOCK-PEER", "Block: below the top 30 % of its BSR peer group", IsBlock: true),
            new SapVerdictRule(SapOutcomePath.BlockAbsolute, Decisions.Block, SapVerdicts.Block, BlockReasonCode, "BLOCK-ABS", "Block: sell-thru below 1 %/day after 4+ weeks on floor", IsBlock: true),
            new SapVerdictRule(SapOutcomePath.BlockBelowEmfMinimum, Decisions.Block, SapVerdicts.Block, BlockReasonCode, "BLOCK-EMF-MIN", "Block: in the top 20 % but below the minimum sell-thru of its Entry / Mid / Fashion type", IsBlock: true),
            new SapVerdictRule(SapOutcomePath.EmfNotMaintained, Decisions.Hold, SapVerdicts.Hold, "EMF_NOT_MAINTAINED", "EMF-00", "Planner has not maintained this MC-MRP in the master (Entry / Mid / Fashion mapping)"),
            new SapVerdictRule(SapOutcomePath.BobsSiblingsOk, Decisions.Allow, SapVerdicts.Allow, "BOBS_SIBLINGS_OK", "BOBS-01", "New colour: all other colours of the option are in the top 20 % and the oldest meets the Entry / Mid / Fashion minimum"),
            new SapVerdictRule(SapOutcomePath.BlockBobsSibling, Decisions.Block, SapVerdicts.Block, BlockReasonCode, "BLOCK-BOBS", "Block: new colour, another colour of the option is below the top 30 %", IsBlock: true),
            new SapVerdictRule(SapOutcomePath.LowHistory, Decisions.Allow, SapVerdicts.Allow, "LOW_HISTORY", "WOF-00", "Too little selling history (weeks on floor below the gate), allowed"),
            new SapVerdictRule(SapOutcomePath.NoPeerPool, Decisions.Allow, SapVerdicts.Allow, "NO_PEER_POOL", "PEER-00", "No BSR peer group available, allowed"),
            new SapVerdictRule(SapOutcomePath.NewArticle, Decisions.AutoAllow, SapVerdicts.Allow, "NEW_ARTICLE", "NEW-00", "New article: not in Universe Master or BSR"),
            new SapVerdictRule(SapOutcomePath.CannotValidate, Decisions.CannotValidate, SapVerdicts.Watch, "CANNOT_VALIDATE", "CV-00", "The tool cannot validate this item (unknown article / missing data)"),
        };

        private static readonly Dictionary<SapOutcomePath, SapVerdictRule> ByPath = Table.ToDictionary(r => r.Path);

        public static SapVerdictRule Rule(SapOutcomePath path) => ByPath[path];

        /// <summary>
        /// The SAP verdict of a rule. Block rows answer BLOCK (default) or HOLD when <paramref name="blockVerdict"/> is
        /// "HOLD" (SapInterface:BlockVerdict, for an SAP side without BLOCK); everything else as in the table.
        /// </summary>
        public static string VerdictFor(SapVerdictRule rule, string? blockVerdict) =>
            rule.IsBlock && string.Equals((blockVerdict ?? "").Trim(), SapVerdicts.Hold, StringComparison.OrdinalIgnoreCase)
                ? SapVerdicts.Hold
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
                // v1.1.6 D1: "No Plan Available" Hold — checked first by the engine
                if (b.NoPlan == true) return SapOutcomePath.NoPlanData;
                // Watch List: colour mix under the minimum, or (v1.1.6 D3) no F3 sale for the MC — same rule
                if (d == Decisions.WatchList) return SapOutcomePath.MixBelowMin;
                if (d == Decisions.Hold || d == Decisions.ReduceRecommended)
                    return b.MaxPermQty is double m && double.IsFinite(m) && m > 0 ? SapOutcomePath.CoverAboveNorm : SapOutcomePath.CoverNoHeadroom;
                // Auto Allow: cover within the norm (v1.1.6: no plan is a Hold, no sale a Watch List — never Auto Allow)
                return SapOutcomePath.CoverWithinNorm;
            }
            if (o.Engine == "A")
            {
                var a = o.A;
                var d = a?.Decision ?? o.Decision;
                var reason = a?.DecisionReason ?? o.DecisionReason ?? "";
                var q = a?.Quartile;
                // Step 3: the 20–30% band (True Repeat or BOBs) → HOLD for the B&M Head
                if (d == Decisions.Exception) return SapOutcomePath.PeerException;
                // Step 2 first: the BOBs reasons of these start like an allowed one ("BOBs: All …")
                if (a?.EmfCheck == Common.EmfMapping.CheckNotMapped) return SapOutcomePath.EmfNotMaintained;
                if (d == Decisions.Block && a?.EmfCheck == Common.EmfMapping.CheckBelow) return SapOutcomePath.BlockBelowEmfMinimum;
                if (d == Decisions.Block)
                {
                    if (reason.StartsWith("Absolute Block override", StringComparison.Ordinal)) return SapOutcomePath.BlockAbsolute;
                    return a?.OptionType == "BOBs" || reason.StartsWith("BOBs: ", StringComparison.Ordinal)
                        ? SapOutcomePath.BlockBobsSibling : SapOutcomePath.BlockBelowQ1;
                }
                if (d == Decisions.Allow || d == Decisions.AutoAllow)
                {
                    if (reason.StartsWith("BOBs: All", StringComparison.Ordinal)) return SapOutcomePath.BobsSiblingsOk;
                    if (reason.Contains("Insufficient", StringComparison.Ordinal) || reason.StartsWith("BOBs: No sibling", StringComparison.Ordinal))
                        return SapOutcomePath.LowHistory;
                    if (reason.Contains("peer pool", StringComparison.OrdinalIgnoreCase)) return SapOutcomePath.NoPeerPool;
                    return q == Tool1EngineA.AllowBand ? SapOutcomePath.PeerQ1 : SapOutcomePath.NoPeerPool;
                }
                return SapOutcomePath.CannotValidate;
            }
            return SapOutcomePath.CannotValidate;
        }
    }
}

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Application.DTOs.Recommendation;

namespace Application.DTOs.Tool1
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Tool 1 "✏️ New Request" API DTOs (Category view). See docs/ui/tool1.md for how
    // the portal renders them.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>One option of a drop-down.</summary>
    public sealed class Tool1OptionDto
    {
        public string Value { get; set; } = "";
        public string Label { get; set; } = "";
    }

    /// <summary>
    /// What the New Request form offers for a GM (ports of <c>formGMInput</c>, <c>formPopulateColours</c>
    /// and <c>populateMonths</c>).
    /// </summary>
    public sealed class Tool1FormOptionsDto
    {
        /// <summary>The trimmed GM code.</summary>
        public string Gm { get; set; } = "";
        /// <summary>True when the GM was resolved (Article Master / Universe Master / F2): show the attribute strip.</summary>
        public bool ShowStrip { get; set; }
        /// <summary>Resolved attributes (null when unknown or the code is shorter than 3 characters).</summary>
        public Tool1InfoDto? Info { get; set; }
        /// <summary>Strip texts as painted: Brand Type, Division, MC CD, MC Desc, MRP ("₹399"), Flag — "—" when blank.</summary>
        public Tool1StripDto? Strip { get; set; }
        /// <summary>
        /// Engine the GM routes to without a colour: "B" (in Universe Master), "A" (in BSR) or "NEW".
        /// For "A" the option type (True Repeat / BOBs) depends on the colour chosen.
        /// </summary>
        public string? EngineRoute { get; set; }
        /// <summary>First (empty-value) option of the colour select: "— enter material first —", "— select colour —" or "— no colours found —".</summary>
        public string ColourPlaceholder { get; set; } = "";
        /// <summary>Colour options (value = code, label = "BLK - BLACK"), already sorted.</summary>
        public List<Tool1OptionDto> Colours { get; set; } = new();
        /// <summary>Label of the trailing "__new__" option ("+ Enter colour manually..." / "+ Enter colour manually"); null when none.</summary>
        public string? NewColourLabel { get; set; }
        /// <summary>True when the manual colour text box must be shown straight away (no colours found).</summary>
        public bool ShowManualColour { get; set; }
        /// <summary>Delivery months: the next 18 months from the server date ("10.2026" / "Oct 2026").</summary>
        public List<Tool1OptionDto> Months { get; set; } = new();
        /// <summary>Required data files not uploaded (Validate would refuse: "Upload required files first: …").</summary>
        public List<string> MissingFiles { get; set; } = new();
    }

    public sealed class Tool1StripDto
    {
        public string Brand { get; set; } = "—";
        public string Div { get; set; } = "—";
        public string McCd { get; set; } = "—";
        public string McDesc { get; set; } = "—";
        public string Mrp { get; set; } = "—";
        public string Flag { get; set; } = "—";
    }

    /// <summary>The New Request form fields.</summary>
    public class Tool1RequestInputDto
    {
        /// <summary>Generic Material code (trimmed by the server).</summary>
        public string? Gm { get; set; }
        /// <summary>Selected colour (option value). Ignored when <see cref="ColourText"/> is used.</summary>
        public string? Colour { get; set; }
        /// <summary>Manually typed colour ("__new__" chosen / no colours found) — trimmed and upper-cased by the server.</summary>
        public string? ColourText { get; set; }
        /// <summary>Delivery month "MM.YYYY".</summary>
        public string? Month { get; set; }
        /// <summary>Proposed quantity (truncated to an integer like parseInt; ≤ 0 is refused).</summary>
        public double? Qty { get; set; }
    }

    /// <summary>Submit = the form fields + the answer to the Confirm Final Quantity pop-up + the ledger sequence seen.</summary>
    public sealed class Tool1SubmitInputDto : Tool1RequestInputDto
    {
        /// <summary>Quantity confirmed in the pop-up (clamped to 0 … Max Permissible, truncated like parseInt).</summary>
        public double? ConfirmedQty { get; set; }
        /// <summary>"✕ Don't Raise" — counts as 0 approved.</summary>
        public bool Declined { get; set; }
        /// <summary>The <c>seenSeq</c> returned by Validate (the ledger the decision was shown against).</summary>
        public int SeenSeq { get; set; }
        /// <summary>Skip the stale-headroom check (shared.js second POST after a 409).</summary>
        public bool Force { get; set; }
    }

    /// <summary>Outcome of Validate / Submit.</summary>
    public sealed class Tool1ResultDto
    {
        /// <summary>"needsConfirmation" (show the pop-up, then Submit), "decided" (Validate: call Submit to save), "saved", "notSaved".</summary>
        public string Status { get; set; } = "";
        public bool NeedsConfirmation { get; set; }
        /// <summary>Pop-up figures (when <see cref="NeedsConfirmation"/>): "You proposed" and "Max Permissible" (also the input's max and default).</summary>
        public double? ProposedQty { get; set; }
        public double? MaxPermissibleQty { get; set; }
        /// <summary>Ledger sequence the decision was computed against — send back as Submit.SeenSeq.</summary>
        public int SeenSeq { get; set; }
        public string Engine { get; set; } = "";
        public string OptionType { get; set; } = "";
        public string Decision { get; set; } = "";
        public string DecisionReason { get; set; } = "";
        /// <summary>The planner's confirmed (or capped) quantity; null when no confirmation applied.</summary>
        public double? FinalApprovedQty { get; set; }
        /// <summary>Quantity that counts in today's ledger (<c>_approvedQtyFor</c>): Single Concept only, else null.</summary>
        public double? ApprovedQty { get; set; }
        /// <summary>True when a parallel approval capped the confirmed quantity (409 re-check).</summary>
        public bool HeadroomChanged { get; set; }
        public string? ConflictByOthers { get; set; }
        /// <summary>Saved request id fields (Submit only).</summary>
        public int? Id { get; set; }
        public string? ReqId { get; set; }
        public int? Seq { get; set; }
        public JsonObject? Approval { get; set; }
        /// <summary>Toast messages, in order.</summary>
        public List<string> Toasts { get; set; } = new();
        /// <summary>Everything the result card shows.</summary>
        public Tool1CardDto? Card { get; set; }
        /// <summary>The v1.1.4 request object (as stored; on Validate, as it would be stored).</summary>
        public JsonObject? Request { get; set; }
    }

    /// <summary>One row of the Today's Requests table (<c>renderCatHistory</c>).</summary>
    public sealed class Tool1HistoryRowDto
    {
        /// <summary>reqId, else id.</summary>
        public string RequestId { get; set; } = "";
        public string Gm { get; set; } = "";
        public string Colour { get; set; } = "";
        public string McDesc { get; set; } = "—";
        /// <summary>"₹399" / "₹—".</summary>
        public string Mrp { get; set; } = "";
        public string Month { get; set; } = "";
        /// <summary>fmt(qty).</summary>
        public string Qty { get; set; } = "";
        /// <summary>"Eng B" / "Eng A" / "Eng NEW"; css class et-b / et-a / et-new.</summary>
        public string EngineTag { get; set; } = "";
        public string EngineClass { get; set; } = "";
        public string OptionType { get; set; } = "";
        public string Decision { get; set; } = "";
        /// <summary>dpill class: dp-allow / dp-reduce / dp-watch / dp-hold / dp-block / dp-nodata.</summary>
        public string DecisionClass { get; set; } = "";
        /// <summary>Approval badge under the pill (Watch List), null when none.</summary>
        public Tool1BadgeDto? ApprovalBadge { get; set; }
        /// <summary>fmt(_approvedQtyFor) or "—".</summary>
        public string ApprovedQty { get; set; } = "—";
        public string By { get; set; } = "—";
        public string? RequestedByEmail { get; set; }
    }

    /// <summary>shared.js <c>approvalBadge</c>: "⏳ Awaiting …", "✓ Approved by …", "✕ Rejected by …", "⊘ Cleared …".</summary>
    public sealed class Tool1BadgeDto
    {
        public string Text { get; set; } = "";
        public string Colour { get; set; } = "";
        public string Background { get; set; } = "";
        /// <summary>Tooltip (approver comment).</summary>
        public string Title { get; set; } = "";
    }

    public sealed class Tool1TodayDto
    {
        /// <summary>Newest first.</summary>
        public List<Tool1HistoryRowDto> Rows { get; set; } = new();
        public int Seq { get; set; }
        /// <summary>Total today's requests visible (the "Session: n" counter).</summary>
        public int Count { get; set; }
    }
}

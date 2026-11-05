using System.Text.Json.Serialization;
using DecisionEngine.Results.Explain;
using DecisionEngine.Results.Recommendation;

namespace DecisionEngine.Results.Tool1
{
    // ─────────────────────────────────────────────────────────────────────────────
    // The Tool 1 result card (index.html renderResult, line 4543) as data: every figure,
    // label, colour and text the card paints, already formatted the way the JS formatted
    // it (fmt = en-IN grouping, toFixed …), so the portal only lays it out.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>A label / value pair with the colour the value is painted in.</summary>
    public sealed class Tool1KvDto
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        /// <summary>CSS colour of the value (hex), null = default ink.</summary>
        public string? Colour { get; set; }
    }

    public sealed class Tool1CardDto
    {
        /// <summary>The number in the header circle (request id).</summary>
        public int Id { get; set; }
        public string Gm { get; set; } = "";
        public string Colour { get; set; } = "";
        /// <summary>"Single Concept MRP · Colour STR" / "Multi Concept MRP · Peer BSR" / "New Product".</summary>
        public string EngineLabel { get; set; } = "";
        public string EngineBg { get; set; } = "";
        public string EngineColour { get; set; } = "";
        /// <summary>Option-type chip (True Repeat / BOBs / New Option); null for Single Concept.</summary>
        public string? OptionTypeChip { get; set; }
        /// <summary>
        /// v1.1.6 S7 (<c>_artLine</c>): the Generic Article description (grey) and vendor under the code; null when the
        /// Article Master gives neither.
        /// </summary>
        public Tool1ArtLineDto? ArtLine { get; set; }
        /// <summary>"NON-PL · MENS · M-TSHIRT RN · ₹399 · NON-BOGO" ("" when the GM is unresolved).</summary>
        public string SubLine { get; set; } = "";
        public string Decision { get; set; } = "";
        /// <summary>Decision pill colours (text, background, border).</summary>
        public string DecisionColour { get; set; } = "";
        public string DecisionBg { get; set; } = "";
        public string DecisionBorder { get; set; } = "";
        /// <summary>Attribute chips: Brand Type, Division, MC, MRP, Flag, Del. Month, Proposed Qty [, Raised Qty (pink #E8006E)].</summary>
        public List<Tool1KvDto> Chips { get; set; } = new();
        public Tool1CardBDto? B { get; set; }
        public Tool1CardADto? A { get; set; }
        /// <summary>"{decision} — Reason".</summary>
        public string ReasonTitle { get; set; } = "";
        public string Reason { get; set; } = "";
        /// <summary>Amber STR caution box (separate from the reason), null when none.</summary>
        public StrCautionDto? StrCaution { get; set; }
        /// <summary>
        /// v1.1.6 S3: the later-PO caution under the reason (single concept, Auto Allow, 1,000 pcs or more too much on a
        /// later 1st) — information only; its working is Step 6 of the step-by-step panel. Null when there is none.
        /// </summary>
        public Tool1LaterCautionDto? LaterCaution { get; set; }
        /// <summary>v1.1.6 S5: the buyer's "Why introduce this colour?" (single concept Watch List), null when none.</summary>
        public string? ColourReason { get; set; }
        /// <summary>v1.1.6 B1: the sibling colours the BOBs rule looked at (BOBs only, display only), null otherwise.</summary>
        public List<Tool1BobsSiblingDto>? BobsSiblings { get; set; }
        /// <summary>v1.1.6 S8: "How was this number worked out?" (single concept with a month-by-month roll), null otherwise.</summary>
        public SingleExplainDto? Explain { get; set; }
        /// <summary>Footer hint left of the "📊 View Recommendation" button.</summary>
        public string FooterHint { get; set; } = "";
        /// <summary>Engine and bucket the View Recommendation button opens.</summary>
        public string RecEngine { get; set; } = "";
        public Tool1RecInfoDto RecInfo { get; set; } = new();
    }

    /// <summary>v1.1.6 <c>_artLine(r)</c>: "{description or —} · {vendor}" with the hover title.</summary>
    public sealed class Tool1ArtLineDto
    {
        /// <summary>Description, "—" when only the vendor is known.</summary>
        public string Description { get; set; } = "";
        /// <summary>Vendor name, null when unknown.</summary>
        public string? Vendor { get; set; }
        /// <summary>Hover text: "{description} · Vendor: {vendor}".</summary>
        public string Title { get; set; } = "";
    }

    /// <summary>A piece of a card text; <see cref="Bold"/> parts were &lt;strong&gt; in the page.</summary>
    public sealed record Tool1TextSegmentDto(string Text, bool Bold = false);

    /// <summary>v1.1.6 <c>_laterPOCaution(r)</c> result with its card texts and Step 6 table.</summary>
    public sealed class Tool1LaterCautionDto
    {
        /// <summary>The later month whose 1st is above the limit ("MM.YYYY", year, month).</summary>
        public string MKey { get; set; } = "";
        public int Y { get; set; }
        public int M { get; set; }
        /// <summary>Stock on that 1st (with this request), the limit (rounded down) and the STR there.</summary>
        public double Stock { get; set; }
        public double Limit { get; set; }
        public double Str { get; set; }
        /// <summary>T1 × (1 + buffer %).</summary>
        public double EffT { get; set; }
        /// <summary>Pcs too much on that 1st (≥ 1,000).</summary>
        public double Excess { get; set; }
        /// <summary>The POs named, latest first.</summary>
        public List<Tool1LaterCautionItemDto> Items { get; set; } = new();
        /// <summary>"With this request, the Jan 2027 PO 9,000 will be 3,445 too much on 1 Feb 2027 (135d vs 79d limit). Consider reducing it to 5,555."</summary>
        public string Text { get; set; } = "";
        /// <summary><see cref="Text"/> in pieces (the bold ones as the page showed them).</summary>
        public List<Tool1TextSegmentDto> Segments { get; set; } = new();
        /// <summary>The small line under the caution.</summary>
        public string Note { get; set; } = "Information only · does not change the decision · working in Step 6 of the step-by-step computation";
        /// <summary>Step 6: heading, limit column header, rows (window start … the month above the limit), working lines, footer.</summary>
        public string Step6Title { get; set; } = "Caution check (information only) — stock on every 1st after the window start, with this request";
        public string LimitHeader { get; set; } = "";
        public List<Tool1Step6RowDto> Rows { get; set; } = new();
        public List<string> Working { get; set; } = new();
        public string Footer { get; set; } = "";
    }

    public sealed class Tool1LaterCautionItemDto
    {
        public string MKey { get; set; } = "";
        public int Y { get; set; }
        public int M { get; set; }
        /// <summary>The PO quantity landing that month, of which <see cref="Requests"/> are tool requests (always 0 in v1.1.6).</summary>
        public double Qty { get; set; }
        public double Requests { get; set; }
        /// <summary>Too much named to this PO, and what is left (<c>keep = qty − cut</c>).</summary>
        public double Cut { get; set; }
        public double Keep { get; set; }
    }

    /// <summary>One row of Step 6 (cells as painted).</summary>
    public sealed class Tool1Step6RowDto
    {
        /// <summary>"1 Nov 2026".</summary>
        public string Label { get; set; } = "";
        /// <summary>The first row (window start, "← Window Start").</summary>
        public bool WindowStart { get; set; }
        /// <summary>The month above the limit (red row).</summary>
        public bool Bad { get; set; }
        public string Background { get; set; } = "";
        public string Stock { get; set; } = "";
        public string Limit { get; set; } = "";
        public string Str { get; set; } = "";
        public string StrColour { get; set; } = "";
        /// <summary>"3,445" on the bad row, else "—".</summary>
        public string Excess { get; set; } = "";
    }

    /// <summary>
    /// v1.1.6 B1 <c>_bobsSibList</c>: one BSR row of a BOBs request's sibling colours (saved with the request as
    /// <c>bobsSiblings</c>; at most 60, ranked first, then most weeks on floor).
    /// </summary>
    public sealed class Tool1BobsSiblingDto
    {
        [JsonPropertyName("gm")] public string Gm { get; set; } = "";
        [JsonPropertyName("colour")] public string Colour { get; set; } = "";
        [JsonPropertyName("wof")] public double Wof { get; set; }
        /// <summary>"Q1" … "Q4", null = under the weeks-on-floor gate (not ranked).</summary>
        [JsonPropertyName("q")] public string? Q { get; set; }
        [JsonPropertyName("pdstr")] public double Pdstr { get; set; }
        [JsonPropertyName("first3")] public double First3 { get; set; }
        [JsonPropertyName("first3st")] public double? First3St { get; set; }
        [JsonPropertyName("season")] public string Season { get; set; } = "";
    }

    public sealed class Tool1RecInfoDto
    {
        public string McCd { get; set; } = "";
        public string Mrp { get; set; } = "";
        public string Brand { get; set; } = "";
        public string Flag { get; set; } = "";
        public string Div { get; set; } = "";
        public string McDesc { get; set; } = "";
    }

    /// <summary>Single Concept part of the card.</summary>
    public sealed class Tool1CardBDto
    {
        /// <summary>v1.1.6 D1: no F4 plan — only <see cref="NoPlanNote"/> is shown (no KPIs, no steps).</summary>
        public bool NoPlan { get; set; }
        public string? NoPlanNote { get; set; }
        /// <summary>Mix gate failed: only the amber banner is shown (no KPIs, no steps).</summary>
        public bool MixFailed { get; set; }
        /// <summary>"⚠ Colour Mix % = 1.2% — below 2% threshold. Broken / cut size likely."</summary>
        public string? MixBanner { get; set; }
        /// <summary>KPI cards: Op Stock, Mix %, Daily Rate, Colour STR, Max Permissible [, Max Permissible (After Raising PO)].</summary>
        public List<Tool1KvDto> Kpis { get; set; } = new();
        /// <summary>Toggle button texts (collapsed / expanded).</summary>
        public string ShowStepsLabel { get; set; } = "▼ Show Step-by-Step Computation — How STR is computed";
        public string HideStepsLabel { get; set; } = "▲ Hide Step-by-Step Computation";
        public Tool1Step1Dto Step1 { get; set; } = new();
        public Tool1Step2Dto Step2 { get; set; } = new();
        public Tool1Step3Dto Step3 { get; set; } = new();
        public Tool1Step4Dto Step4 { get; set; } = new();
        public Tool1Step5Dto Step5 { get; set; } = new();
        /// <summary>v1.1.6 S3: Step 6 = the later-PO caution working (same object as the card's caution), null when none.</summary>
        public Tool1LaterCautionDto? Step6 { get; set; }
    }

    public sealed class Tool1Step1Dto
    {
        public string Title { get; set; } = "Opening Stock (F1) — As of today";
        /// <summary>"12,345 units"; null when the chain is empty (box hidden).</summary>
        public string? OpStock { get; set; }
        /// <summary>KEY-3 used: "Brand-MC-MRP-Flag-Colour" or "—".</summary>
        public string Key3 { get; set; } = "";
    }

    public sealed class Tool1Step2Dto
    {
        public string Title { get; set; } = "Colour Mix % (F3 Sales Trend)";
        /// <summary>"across Day 1 to Day 45 in F3 (sum of all day-columns)" or "over last N=2 months from F3".</summary>
        public string Basis { get; set; } = "";
        /// <summary>"day" or "month" (labels "Colour Avg Sales/day" …).</summary>
        public string Per { get; set; } = "";
        public string ColAvg { get; set; } = "";
        public string McAvg { get; set; } = "";
        public string MixPct { get; set; } = "";
        /// <summary>"2% ✓ OK" / "2% ✗ FAIL".</summary>
        public string Threshold { get; set; } = "";
        public bool ThresholdOk { get; set; }
    }

    /// <summary>One row of the Step 3 stock table (cells as painted).</summary>
    public sealed class Tool1StockRowDto
    {
        /// <summary>"Oct 2026".</summary>
        public string Month { get; set; } = "";
        /// <summary>"(current month)", "← Delivery Month", "← Window Start" or null.</summary>
        public string? Tag { get; set; }
        public string OpenStock { get; set; } = "";
        /// <summary>"+1,200" or "—".</summary>
        public string ExistingPo { get; set; } = "";
        public string Proposed { get; set; } = "";
        /// <summary>"−1,234".</summary>
        public string Demand { get; set; } = "";
        /// <summary>"(17/31d)" / "(17/31d remaining)" after the demand, or null.</summary>
        public string? DemandNote { get; set; }
        public string CloseStock { get; set; } = "";
        public string Note { get; set; } = "";
        /// <summary>Note colour (#DC2626 breach / #00873E ok / #6B7280).</summary>
        public string NoteColour { get; set; } = "";
        /// <summary>Row background: #FFF3CD current pre-roll month, #FFF0F7 delivery month, #EAF0FB window start, #fff.</summary>
        public string Background { get; set; } = "";
        public bool Bold { get; set; }
        /// <summary>True for pre-roll months (before the delivery month).</summary>
        public bool PreRoll { get; set; }
    }

    public sealed class Tool1Step3Dto
    {
        public string Title { get; set; } = "Forward Stock Build — Pre-roll to Delivery Month";
        /// <summary>fmt(proposed qty) used in the description.</summary>
        public string ProposedQty { get; set; } = "";
        /// <summary>Pre-roll months, then the first 6 chain months. Empty → "No chain data available".</summary>
        public List<Tool1StockRowDto> Rows { get; set; } = new();
    }

    public sealed class Tool1WindowRowDto
    {
        public string Month { get; set; } = "";
        public string Demand { get; set; } = "";
        public string DaysInMonth { get; set; } = "";
        /// <summary>"31" or "13 (partial)" (partial is bold amber).</summary>
        public string DaysTaken { get; set; } = "";
        public bool Partial { get; set; }
        public string PeriodDemand { get; set; } = "";
        /// <summary>#fff / #f9f5ff stripes.</summary>
        public string Background { get; set; } = "";
    }

    public sealed class Tool1Step4Dto
    {
        /// <summary>v1.1.6 S4: "STR Calculation — {D}-Day Window from Day 1 of the Month after Delivery".</summary>
        public string Title { get; set; } = "";
        /// <summary>D (days) shown in the description and the TOTAL row.</summary>
        public string D { get; set; } = "";
        /// <summary>Window rows (only when the chain has more than one month).</summary>
        public List<Tool1WindowRowDto> Rows { get; set; } = new();
        /// <summary>TOTAL row: "TOTAL (75 days)", period demand total.</summary>
        public string? TotalLabel { get; set; }
        public string? TotalDemand { get; set; }
        /// <summary>The 5 lines of the "STR Formula" box.</summary>
        public List<string> Formula { get; set; } = new();
        /// <summary>Colour of the STR figure / result (#DC2626 breach, #00873E ok).</summary>
        public string StrColour { get; set; } = "";
        public bool Breach { get; set; }
    }

    public sealed class Tool1Step5Dto
    {
        public string Title { get; set; } = "Max Permissible Qty & Final Decision";
        /// <summary>Header / panel colours (#DC2626 + #FEF2F2 breach, #00873E + #DCFCE7 ok).</summary>
        public string HeaderColour { get; set; } = "";
        public string PanelBg { get; set; } = "";
        /// <summary>The 4 formula lines (v1.1.4 README item 2: exact effective threshold and the engine's stock before this PO).</summary>
        public List<string> Formula { get; set; } = new();
        /// <summary>Colour of the result figure (#00873E when &gt; 0, else #DC2626).</summary>
        public string ResultColour { get; set; } = "";
        /// <summary>"HOLD", "ALLOW" or "REDUCE".</summary>
        public string Verdict { get; set; } = "";
        public string VerdictColour { get; set; } = "";
        /// <summary>Bold lead ("HOLD — Do not raise PO.") and the rest of the text.</summary>
        public string VerdictLead { get; set; } = "";
        public string VerdictText { get; set; } = "";
    }

    /// <summary>Multi Concept part of the card (Steps 1–4 + absolute block note).</summary>
    public sealed class Tool1CardADto
    {
        /// <summary>Step 1 — Article &amp; Peer Pool (5 tiles; keys contain "\n" line breaks).</summary>
        public List<Tool1KvDto> Step1 { get; set; } = new();
        /// <summary>Step 2 — PDSTR vs Peer Average (4 tiles), null when PDSTR is unknown.</summary>
        public List<Tool1KvDto>? Step2 { get; set; }
        public Tool1QuartileDto? Step3 { get; set; }
        public Tool1BorderDto? Step4 { get; set; }
        /// <summary>"⚡ Absolute Block Override: …" or null.</summary>
        public string? AbsoluteBlockNote { get; set; }
    }

    public sealed class Tool1QuartileDto
    {
        /// <summary>"Step 3 — Quartile Ranking (63th pct — 12 styles)".</summary>
        public string Heading { get; set; } = "";
        /// <summary>"Rank within peer group of 12 styles".</summary>
        public string PoolText { get; set; } = "";
        /// <summary>"63th percentile — Q2 — Healthy".</summary>
        public string RankText { get; set; } = "";
        public string RankColour { get; set; } = "";
        /// <summary>Marker position on the bar, 2 … 98 (%).</summary>
        public double Position { get; set; }
        public double Percentile { get; set; }
    }

    public sealed class Tool1BorderDto
    {
        /// <summary>"Threshold (Q4 &amp; Q3): gap ≤ 10%", "This article gap  = 4.2%", "Result = ✓ IN BORDER ZONE".</summary>
        public List<string> Lines { get; set; } = new();
        public bool InBorderZone { get; set; }
        /// <summary>The verdict strip text.</summary>
        public string Verdict { get; set; } = "";
    }
}

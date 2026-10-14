using Application.DTOs.Recommendation;

namespace Application.DTOs.Tool1
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
        /// <summary>Footer hint left of the "📊 View Recommendation" button.</summary>
        public string FooterHint { get; set; } = "";
        /// <summary>Engine and bucket the View Recommendation button opens.</summary>
        public string RecEngine { get; set; } = "";
        public Tool1RecInfoDto RecInfo { get; set; } = new();
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
        public string Title { get; set; } = "STR Calculation — 60-Day Window from Day 1 of Delivery Month";
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

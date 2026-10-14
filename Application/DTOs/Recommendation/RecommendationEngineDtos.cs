using System.Text.Json.Serialization;

namespace Application.DTOs.Recommendation
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Engine result DTOs. Property JSON names are the exact field names of the
    // objects the JS returns (index.html second <script>, lines 9369–11512), so the
    // API payload has the same shape as the browser's _runA/_runB results and the
    // parity tests can compare them field by field. Absent JS fields (undefined)
    // are null here.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>One month of the rolling stock chain ({y,m,mkey,openStock,poIn,colPlan,closeStock}).</summary>
    public sealed class ChainStepDto
    {
        [JsonPropertyName("y")] public int Y { get; set; }
        [JsonPropertyName("m")] public int M { get; set; }
        [JsonPropertyName("mkey")] public string MKey { get; set; } = "";
        [JsonPropertyName("openStock")] public double OpenStock { get; set; }
        [JsonPropertyName("poIn")] public double PoIn { get; set; }
        [JsonPropertyName("colPlan")] public double ColPlan { get; set; }
        [JsonPropertyName("closeStock")] public double CloseStock { get; set; }
    }

    /// <summary>
    /// Result of <c>_coreColourSTRFast</c> (index.html line 10329): rolling-window STR (days) and Max
    /// Permissible qty for one colour / attribute / bucket anchored to one month.
    /// </summary>
    public sealed class CoreStrResultDto
    {
        /// <summary>Rounded STR in days; null when the window has no demand ("No data").</summary>
        [JsonPropertyName("str")] public double? Str { get; set; }
        /// <summary>Window daily rate, 1 dp.</summary>
        [JsonPropertyName("dr")] public double Dr { get; set; }
        /// <summary>Max Permissible qty (floored).</summary>
        [JsonPropertyName("rec")] public double Rec { get; set; }
        /// <summary>Rounded effective threshold (T × (1+buffer)); null for "No data".</summary>
        [JsonPropertyName("eff")] public double? Eff { get; set; }
        /// <summary>Unrounded effective threshold; null for "No data".</summary>
        [JsonPropertyName("effExact")] public double? EffExact { get; set; }
        /// <summary>"Raise", "Hold" or "No data".</summary>
        [JsonPropertyName("action")] public string Action { get; set; } = "";
        [JsonPropertyName("opStock")] public double OpStock { get; set; }
        /// <summary>Mix ratio in % (1 dp).</summary>
        [JsonPropertyName("mixPct")] public double MixPct { get; set; }
        [JsonPropertyName("winStockStart")] public double WinStockStart { get; set; }
        [JsonPropertyName("remForecast")] public double RemForecast { get; set; }
        [JsonPropertyName("chain")] public List<ChainStepDto> Chain { get; set; } = new();
        [JsonPropertyName("winDays")] public double WinDays { get; set; }
        /// <summary>Index of the window-start month in <see cref="Chain"/> (always 1).</summary>
        [JsonPropertyName("d1Idx")] public int D1Idx { get; set; }
    }

    /// <summary>Result of <c>_recQty</c> (index.html line 10178) — simple month STR / rec qty.</summary>
    public sealed class RecQtyResultDto
    {
        [JsonPropertyName("rec")] public double Rec { get; set; }
        [JsonPropertyName("str")] public double? Str { get; set; }
        [JsonPropertyName("dr")] public double Dr { get; set; }
        [JsonPropertyName("eff")] public double? Eff { get; set; }
        [JsonPropertyName("action")] public string Action { get; set; } = "";
    }

    // ── Single Concept (_runB) ─────────────────────────────────────────────

    /// <summary>Result of <c>_runB</c> (index.html line 10441).</summary>
    public sealed class RecBResultDto
    {
        /// <summary>One row per colour, sorted Stock high → low.</summary>
        [JsonPropertyName("rows")] public List<RecBRowDto> Rows { get; set; } = new();
        /// <summary>Bucket 45-day sales.</summary>
        [JsonPropertyName("totalS")] public double TotalS { get; set; }
        /// <summary>Rounded effective threshold (EffT).</summary>
        [JsonPropertyName("eff")] public double Eff { get; set; }
    }

    public sealed class RecBRowDto
    {
        /// <summary>Normalised (lower-case) colour code; the screen shows it upper-cased.</summary>
        [JsonPropertyName("col")] public string Col { get; set; } = "";
        [JsonPropertyName("stk")] public double Stk { get; set; }
        [JsonPropertyName("s45")] public double S45 { get; set; }
        /// <summary>Sale Mix% (1 dp).</summary>
        [JsonPropertyName("mix")] public double Mix { get; set; }
        /// <summary>Stock Mix% (1 dp).</summary>
        [JsonPropertyName("stkmix")] public double StkMix { get; set; }
        /// <summary>Stock ÷ (45D sales / 45), rounded; null without sales.</summary>
        [JsonPropertyName("strRos")] public double? StrRos { get; set; }
        [JsonPropertyName("watchList")] public bool WatchList { get; set; }
        [JsonPropertyName("watchReason")] public string? WatchReason { get; set; }
        [JsonPropertyName("mdata")] public List<RecBMonthDto> MData { get; set; } = new();
    }

    public sealed class RecBMonthDto
    {
        [JsonPropertyName("mon")] public string Mon { get; set; } = "";
        /// <summary>F2 pending PO landing in this month (incl. previous-month carry-over), excluding today's approvals.</summary>
        [JsonPropertyName("pend")] public double Pend { get; set; }
        /// <summary>Quantity approved today in Tool 1 (all users) landing in this month.</summary>
        [JsonPropertyName("appr")] public double Appr { get; set; }
        /// <summary>Max Permissible qty.</summary>
        [JsonPropertyName("rec")] public double Rec { get; set; }
        [JsonPropertyName("str")] public double? Str { get; set; }
        [JsonPropertyName("dr")] public double Dr { get; set; }
        [JsonPropertyName("eff")] public double? Eff { get; set; }
        /// <summary>"Raise", "Hold", "No data" or "Watch List".</summary>
        [JsonPropertyName("action")] public string Action { get; set; } = "";
        /// <summary>
        /// Display only (not in the JS object): the Max Permissible cell's tooltip,
        /// "Based on threshold STR of {eff}d, the max permissible qty is {rec}" (null for Watch List —
        /// the row's <see cref="RecBRowDto.WatchReason"/> is the tooltip then).
        /// </summary>
        [JsonPropertyName("tip")] public string? Tip { get; set; }
    }

    // ── Multi Concept (_runA) ──────────────────────────────────────────────

    /// <summary>
    /// Result of <c>_runA</c> (index.html line 10593). One of three shapes: <see cref="Err"/> set;
    /// <see cref="DimMode"/> = "colour-only" with <see cref="Data"/> (a _runB result); or the full
    /// attribute → colour → GM result.
    /// </summary>
    public sealed class RecAResultDto
    {
        [JsonPropertyName("err")] public string? Err { get; set; }
        [JsonPropertyName("dimMode")] public string? DimMode { get; set; }
        [JsonPropertyName("data")] public RecBResultDto? Data { get; set; }

        /// <summary>Assessment dimension used (Style / Fit / Product Material / Design or the override).</summary>
        [JsonPropertyName("dim")] public string? Dim { get; set; }
        /// <summary>Attributes sorted by Deviation% high → low (Step 1 rows).</summary>
        [JsonPropertyName("attrs")] public List<RecAttrDto>? Attrs { get; set; }
        /// <summary>Recommendations, Q1 pass then Q2 pass (Step 2+3 rows).</summary>
        [JsonPropertyName("recs")] public List<RecARecDto>? Recs { get; set; }
        [JsonPropertyName("eff")] public double? Eff { get; set; }
        /// <summary>Whole-bucket STR (the Step 1 "Total" row).</summary>
        [JsonPropertyName("bucketSTR")] public BucketStrDto? BucketStr { get; set; }
        /// <summary>attribute → colour → month → pending PO (incl. today's approvals).</summary>
        [JsonPropertyName("f2ByAttr")] public Dictionary<string, Dictionary<string, Dictionary<string, double>>>? F2ByAttr { get; set; }
        [JsonPropertyName("optLookups")] public OptionLookupsDto? OptLookups { get; set; }
        [JsonPropertyName("anchorMonth")] public string? AnchorMonth { get; set; }
        [JsonPropertyName("months")] public List<string>? Months { get; set; }
        [JsonPropertyName("mc")] public string? Mc { get; set; }
        [JsonPropertyName("mrp")] public string? Mrp { get; set; }
        [JsonPropertyName("brand")] public string? Brand { get; set; }
        [JsonPropertyName("flag")] public string? Flag { get; set; }
    }

    /// <summary>One attribute value (Step 1 row).</summary>
    public sealed class RecAttrDto
    {
        [JsonPropertyName("av")] public string Av { get; set; } = "";
        /// <summary>Attribute STR at the anchor month (9999 = no data).</summary>
        [JsonPropertyName("str")] public double Str { get; set; }
        /// <summary>Sale Mix% (1 dp).</summary>
        [JsonPropertyName("smix")] public double SMix { get; set; }
        /// <summary>Stock Mix% (1 dp).</summary>
        [JsonPropertyName("stkmix")] public double StkMix { get; set; }
        /// <summary>Deviation% = Sale Mix% − Stock Mix% (2 dp).</summary>
        [JsonPropertyName("dev")] public double Dev { get; set; }
        /// <summary>colour → {s45, stk, gms} (JS object order).</summary>
        [JsonPropertyName("cols")] public Dictionary<string, AttrColDto> Cols { get; set; } = new();
        [JsonPropertyName("s45")] public double S45 { get; set; }
        [JsonPropertyName("stk")] public double Stk { get; set; }
        [JsonPropertyName("attrForecastByMon")] public Dictionary<string, double> AttrForecastByMon { get; set; } = new();
        [JsonPropertyName("attrDR")] public double AttrDR { get; set; }
        /// <summary>Per selected month (re-anchored) STR trace — "Attr Op STR" columns + breakdown.</summary>
        [JsonPropertyName("amdata")] public List<AnchorMonthDto> AmData { get; set; } = new();
        /// <summary>Every colour under this attribute ranked by STR ascending.</summary>
        [JsonPropertyName("colRanked")] public List<ColRankDto> ColRanked { get; set; } = new();
    }

    public sealed class AttrColDto
    {
        [JsonPropertyName("s45")] public double S45 { get; set; }
        [JsonPropertyName("stk")] public double Stk { get; set; }
        /// <summary>Normalised GMs in first-seen order.</summary>
        [JsonPropertyName("gms")] public List<string> Gms { get; set; } = new();
    }

    /// <summary>An "amdata" entry: STR re-anchored to one selected month.</summary>
    public sealed class AnchorMonthDto
    {
        [JsonPropertyName("mon")] public string Mon { get; set; } = "";
        /// <summary>Pending PO landing in this month.</summary>
        [JsonPropertyName("pend")] public double Pend { get; set; }
        [JsonPropertyName("str")] public double? Str { get; set; }
        [JsonPropertyName("dr")] public double? Dr { get; set; }
        [JsonPropertyName("winStockStart")] public double? WinStockStart { get; set; }
        [JsonPropertyName("chain")] public List<ChainStepDto>? Chain { get; set; }
        [JsonPropertyName("d1Idx")] public int? D1Idx { get; set; }
    }

    public sealed class ColRankDto
    {
        [JsonPropertyName("col")] public string Col { get; set; } = "";
        /// <summary>Colour STR at the anchor month (9999 = no data).</summary>
        [JsonPropertyName("str")] public double Str { get; set; }
        [JsonPropertyName("stk")] public double Stk { get; set; }
        [JsonPropertyName("s45")] public double S45 { get; set; }
        [JsonPropertyName("gms")] public List<string> Gms { get; set; } = new();
        [JsonPropertyName("_cs")] public CoreStrResultDto? Cs { get; set; }
    }

    /// <summary>One Step 2+3 recommendation (colour × GM).</summary>
    public sealed class RecARecDto
    {
        [JsonPropertyName("attr")] public string Attr { get; set; } = "";
        [JsonPropertyName("attrSTR")] public double AttrStr { get; set; }
        [JsonPropertyName("attrSmix")] public double AttrSmix { get; set; }
        [JsonPropertyName("attrStkmix")] public double AttrStkmix { get; set; }
        [JsonPropertyName("attrDev")] public double AttrDev { get; set; }
        [JsonPropertyName("attrAmdata")] public List<AnchorMonthDto> AttrAmdata { get; set; } = new();
        [JsonPropertyName("col")] public string Col { get; set; } = "";
        [JsonPropertyName("colSTR")] public double ColStr { get; set; }
        /// <summary>Sale Mix% within the attribute (1 dp).</summary>
        [JsonPropertyName("colMixInAttr")] public double ColMixInAttr { get; set; }
        /// <summary>Stock Mix% within the attribute (1 dp).</summary>
        [JsonPropertyName("colStockMixInAttr")] public double ColStockMixInAttr { get; set; }
        [JsonPropertyName("colDev")] public double ColDev { get; set; }
        [JsonPropertyName("optionStockQty")] public double OptionStockQty { get; set; }
        [JsonPropertyName("colStockQty")] public double ColStockQty { get; set; }
        [JsonPropertyName("attrStockQty")] public double AttrStockQty { get; set; }
        [JsonPropertyName("gmDesc")] public string GmDesc { get; set; } = "";
        [JsonPropertyName("vendorCd")] public string VendorCd { get; set; } = "";
        [JsonPropertyName("vendorName")] public string VendorName { get; set; } = "";
        /// <summary>Normalised (lower-case) Generic Material picked from BSR.</summary>
        [JsonPropertyName("gm")] public string Gm { get; set; } = "";
        /// <summary>"Q1" or "Q2".</summary>
        [JsonPropertyName("q")] public string Q { get; set; } = "";
        /// <summary>PDSTR %/day of the picked GM (1 dp).</summary>
        [JsonPropertyName("pdstr")] public double Pdstr { get; set; }
        /// <summary>"P1" (Q1) or "P2" (Q2).</summary>
        [JsonPropertyName("priority")] public string Priority { get; set; } = "";
        [JsonPropertyName("mdata")] public List<RecAMonthDto> MData { get; set; } = new();
    }

    public sealed class RecAMonthDto
    {
        [JsonPropertyName("mon")] public string Mon { get; set; } = "";
        /// <summary>Colour PO Qty (this attribute's GMs, this colour, this month).</summary>
        [JsonPropertyName("pend")] public double Pend { get; set; }
        /// <summary>Option PO Qty (this exact GM + colour, this month).</summary>
        [JsonPropertyName("optPend")] public double OptPend { get; set; }
        /// <summary>Recommended qty.</summary>
        [JsonPropertyName("rec")] public double Rec { get; set; }
        [JsonPropertyName("str")] public double? Str { get; set; }
        [JsonPropertyName("dr")] public double Dr { get; set; }
        [JsonPropertyName("eff")] public double? Eff { get; set; }
        [JsonPropertyName("effExact")] public double? EffExact { get; set; }
        [JsonPropertyName("action")] public string Action { get; set; } = "";
        [JsonPropertyName("stk")] public double? Stk { get; set; }
        [JsonPropertyName("mixPct")] public double? MixPct { get; set; }
        [JsonPropertyName("remForecast")] public double? RemForecast { get; set; }
        [JsonPropertyName("winStockStart")] public double? WinStockStart { get; set; }
        [JsonPropertyName("chain")] public List<ChainStepDto>? Chain { get; set; }
        [JsonPropertyName("d1Idx")] public int? D1Idx { get; set; }
        /// <summary>
        /// Display only: the qty pop-up's Formula cell, "{effExact 2dp} × {dr} − {winStockStart} = {rec}"
        /// ("—" without data).
        /// </summary>
        [JsonPropertyName("formula")] public string? Formula { get; set; }
    }

    /// <summary>Whole-bucket STR (Total row).</summary>
    public sealed class BucketStrDto
    {
        [JsonPropertyName("str")] public double? Str { get; set; }
        [JsonPropertyName("stk")] public double Stk { get; set; }
        [JsonPropertyName("s45")] public double S45 { get; set; }
        /// <summary>Cumulative pending PO up to the anchor month.</summary>
        [JsonPropertyName("pend")] public double Pend { get; set; }
        [JsonPropertyName("dr")] public double Dr { get; set; }
        [JsonPropertyName("remForecast")] public double RemForecast { get; set; }
        [JsonPropertyName("winStockStart")] public double WinStockStart { get; set; }
        [JsonPropertyName("chain")] public List<ChainStepDto> Chain { get; set; } = new();
        [JsonPropertyName("d1Idx")] public int D1Idx { get; set; }
        [JsonPropertyName("anchorMonth")] public string AnchorMonth { get; set; } = "";
        [JsonPropertyName("amdata")] public List<AnchorMonthDto> AmData { get; set; } = new();
    }

    /// <summary>Option-level (GM + colour) lookups (<c>_optionLookups</c>, index.html line 10565).</summary>
    public sealed class OptionLookupsDto
    {
        /// <summary>"gm|colour" → stock.</summary>
        [JsonPropertyName("stkByOpt")] public Dictionary<string, double> StkByOpt { get; set; } = new();
        /// <summary>"gm|colour" → month → pending PO.</summary>
        [JsonPropertyName("f2ByOpt")] public Dictionary<string, Dictionary<string, double>> F2ByOpt { get; set; } = new();
        /// <summary>gm → Article Master description / vendor.</summary>
        [JsonPropertyName("amInfo")] public Dictionary<string, AmInfoDto> AmInfo { get; set; } = new();
    }

    public sealed class AmInfoDto
    {
        [JsonPropertyName("artDesc")] public string ArtDesc { get; set; } = "";
        [JsonPropertyName("vendor")] public string Vendor { get; set; } = "";
        [JsonPropertyName("vendorName")] public string VendorName { get; set; } = "";
    }

    /// <summary>One BSR check (<c>_bsrQ</c>, index.html line 10030).</summary>
    public sealed class BsrQDto
    {
        [JsonPropertyName("wof")] public double Wof { get; set; }
        [JsonPropertyName("pdstr")] public double Pdstr { get; set; }
        [JsonPropertyName("q")] public string Q { get; set; } = "";
    }

    // ── STR caution (Tool 1) ───────────────────────────────────────────────

    /// <summary>
    /// Result of <c>_strCaution</c> (index.html line 9935) plus the texts of <c>_cautionBoxHtml</c>
    /// (line 9998). Null when neither the attribute nor the colour STR is above the Tool 2 threshold.
    /// </summary>
    public sealed class StrCautionDto
    {
        /// <summary>Rounded Tool 2 threshold (T2 × (1+buffer)).</summary>
        [JsonPropertyName("effT2")] public double EffT2 { get; set; }
        [JsonPropertyName("delMonth")] public string DelMonth { get; set; } = "";
        [JsonPropertyName("items")] public List<StrCautionItemDto> Items { get; set; } = new();
    }

    public sealed class StrCautionItemDto
    {
        /// <summary>"Attribute (Regular)" or "Colour (BLK)".</summary>
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("str")] public double Str { get; set; }
        [JsonPropertyName("cs")] public CoreStrResultDto Cs { get; set; } = new();
        /// <summary>Box line: "{label} STR = {str}d for {delMonth}, above Tool 2 threshold of {effT2}d."</summary>
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        /// <summary>Breakdown footer: "Window Start {fmt} ÷ Daily Rate {dr} = {str}d".</summary>
        [JsonPropertyName("footer")] public string Footer { get; set; } = "";
    }

    /// <summary>One option of the dimension-override drop-down (<c>_buildDimOverrideOptions</c>).</summary>
    public sealed class DimOptionDto
    {
        [JsonPropertyName("value")] public string Value { get; set; } = "";
        /// <summary>"Fit (Relevant)" for the Attribute Relevance Master's dimension.</summary>
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("selected")] public bool Selected { get; set; }
    }
}

namespace DecisionEngine.Results.Analytics.AttrAnalysis
{
    /// <summary>Filters of the Brand → MC level (aa-filter-brand / aa-filter-mc / aa-filter-minstk).</summary>
    public sealed class AttrAnalysisQuery
    {
        /// <summary>Exact Brand Type ("" = All Brands).</summary>
        public string? Brand { get; set; }
        /// <summary>Substring of MC CD or MC Desc (case-insensitive, trimmed).</summary>
        public string? Mc { get; set; }
        /// <summary>Minimum stock as typed (parsed like <c>_sf</c>; commas allowed).</summary>
        public string? MinStock { get; set; }
    }

    /// <summary><c>_aaStrColor</c>: STR band colour, light background and label.</summary>
    public sealed class AaStatusDto
    {
        public string Color { get; set; } = "";
        public string Background { get; set; } = "";
        public string Label { get; set; } = "";
    }

    public sealed class AaLegendItemDto
    {
        public AaLegendItemDto() { }
        public AaLegendItemDto(string label, string color) { Label = label; Color = color; }
        public string Label { get; set; } = "";
        public string Color { get; set; } = "";
    }

    /// <summary>A breadcrumb part; <see cref="Level"/> 1 / 2 = link back to that level, null = current (plain text).</summary>
    public sealed class AaCrumbDto
    {
        public AaCrumbDto() { }
        public AaCrumbDto(string text, int? level) { Text = text; Level = level; }
        public string Text { get; set; } = "";
        public int? Level { get; set; }
    }

    public sealed class AaKvDto
    {
        public AaKvDto() { }
        public AaKvDto(string label, string value) { Label = label; Value = value; }
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
    }

    /// <summary>Level 1 row — one Brand × MC (click → level 2 with Brand + McKey).</summary>
    public sealed class AaMcRowDto
    {
        public int Index { get; set; }
        public string Brand { get; set; } = "";
        /// <summary>"{MC CD}|||{MC Desc}" — the key used to drill down.</summary>
        public string McKey { get; set; } = "";
        public string McCd { get; set; } = "";
        public string McDesc { get; set; } = "";
        public double Stock { get; set; }
        public string StockText { get; set; } = "";
        public double Sales45 { get; set; }
        public string Sales45Text { get; set; } = "";
        public int Buckets { get; set; }
        /// <summary>Implied STR in days (stock ÷ 45-day daily rate); null = no sales.</summary>
        public double? ImpliedStr { get; set; }
        public AaStatusDto Status { get; set; } = new();
        /// <summary>Row tooltip: "Implied STR: 258d — Very Slow" / "Implied STR: No Sales".</summary>
        public string Tooltip { get; set; } = "";
    }

    public sealed class AaLevel1Dto
    {
        /// <summary>"noData" (no F1 and no F3), "noMatch" (filters exclude everything) or "ok".</summary>
        public string State { get; set; } = "ok";
        public string? EmptyTitle { get; set; }
        public string? EmptyMessage { get; set; }
        /// <summary>Brand filter options (sorted; "All Brands" = "").</summary>
        public List<string> Brands { get; set; } = new();
        public List<AaCrumbDto> Breadcrumb { get; set; } = new();
        public List<AaLegendItemDto> Legend { get; set; } = new();
        public List<AaMcRowDto> Rows { get; set; } = new();
    }

    /// <summary>Level 2 row — one MRP × Flag bucket (click → level 3).</summary>
    public sealed class AaBucketRowDto
    {
        public int Index { get; set; }
        public string Mrp { get; set; } = "";
        /// <summary>"₹" + MRP.</summary>
        public string MrpText { get; set; } = "";
        public string Flag { get; set; } = "";
        public double Stock { get; set; }
        public string StockText { get; set; } = "";
        public double Sales45 { get; set; }
        public string Sales45Text { get; set; } = "";
        public int Colours { get; set; }
        public double? ImpliedStr { get; set; }
        public AaStatusDto Status { get; set; } = new();
        public string Tooltip { get; set; } = "";
    }

    public sealed class AaLevel2Dto
    {
        public string Brand { get; set; } = "";
        public string McKey { get; set; } = "";
        public string McCd { get; set; } = "";
        public string McDesc { get; set; } = "";
        public List<AaCrumbDto> Breadcrumb { get; set; } = new();
        public List<AaLegendItemDto> Legend { get; set; } = new();
        public List<AaBucketRowDto> Rows { get; set; } = new();
    }

    /// <summary>A row of an attribute table (Sales Mix vs Stock Mix).</summary>
    public sealed class AaAttrRowDto
    {
        public string Value { get; set; } = "";
        public double Stock { get; set; }
        public string StockText { get; set; } = "";
        public double StockMix { get; set; }
        public string StockMixText { get; set; } = "";
        public double Sales45 { get; set; }
        public string Sales45Text { get; set; } = "";
        public double SalesMix { get; set; }
        public string SalesMixText { get; set; } = "";
        public double? Deviation { get; set; }
        /// <summary>"+5.2pp" / "-2.5pp" (Total row: "—").</summary>
        public string DeviationText { get; set; } = "";
        /// <summary><c>_aaDevColor</c> (null on the Total row).</summary>
        public string? DeviationColor { get; set; }
        /// <summary>"(No {dim} tag in Article Master)" row — grey italic.</summary>
        public bool Unmapped { get; set; }
    }

    public sealed class AaAttrTableDto
    {
        public string Dimension { get; set; } = "";
        /// <summary>"{dim} — Sales Mix vs Stock Mix".</summary>
        public string Title { get; set; } = "";
        public List<AaAttrRowDto> Rows { get; set; } = new();
        /// <summary>"Total (bucket)" footer (bold, top border).</summary>
        public AaAttrRowDto Total { get; set; } = new();
    }

    /// <summary>Colour-level STR band card (left border = Color).</summary>
    public sealed class AaBandDto
    {
        public string Label { get; set; } = "";
        public string Color { get; set; } = "";
        public int Count { get; set; }
        public double Stock { get; set; }
        /// <summary>"{fmt(stock)} units".</summary>
        public string StockText { get; set; } = "";
    }

    public sealed class AaColourRowDto
    {
        public string Colour { get; set; } = "";
        public double Stock { get; set; }
        public string StockText { get; set; } = "";
        public double StockMix { get; set; }
        public string StockMixText { get; set; } = "";
        public double Sales45 { get; set; }
        public string Sales45Text { get; set; } = "";
        public double SalesMix { get; set; }
        public string SalesMixText { get; set; } = "";
        /// <summary>Math.round(stock ÷ daily rate); null = no sales.</summary>
        public double? Str { get; set; }
        /// <summary>"656d" or "—".</summary>
        public string StrText { get; set; } = "";
        public AaStatusDto Status { get; set; } = new();
    }

    public sealed class AaLevel3Dto
    {
        public string Brand { get; set; } = "";
        public string McKey { get; set; } = "";
        public string McCd { get; set; } = "";
        public string McDesc { get; set; } = "";
        public string Mrp { get; set; } = "";
        public string Flag { get; set; } = "";
        public List<AaCrumbDto> Breadcrumb { get; set; } = new();
        /// <summary>Key-value chips: Brand Type, MC, MC Desc, MRP, Flag, Total Stock, Total Sales (45d).</summary>
        public List<AaKvDto> Summary { get; set; } = new();
        public double TotalStock { get; set; }
        public double TotalSales45 { get; set; }
        /// <summary>Amber note (Article Master not loaded) or grey note (no attribute columns); null = none.</summary>
        public string? Note { get; set; }
        /// <summary>"warning" (amber card #FFFBEB / #FDE68A / #92400E) or "info" (grey text).</summary>
        public string? NoteKind { get; set; }
        /// <summary>Attribute columns found in Article Master (<c>_aaDiscoverAttrCols</c>).</summary>
        public List<string> AttributeColumns { get; set; } = new();
        /// <summary>Deviation legend (shown when there is at least one attribute column).</summary>
        public List<AaLegendItemDto> DeviationLegend { get; set; } = new();
        /// <summary>One table per attribute column that has values in this bucket.</summary>
        public List<AaAttrTableDto> AttributeTables { get; set; } = new();
        /// <summary>"Colour-level STR — bucket: ₹{mrp} · {flag}".</summary>
        public string ColourTitle { get; set; } = "";
        public List<AaBandDto> Bands { get; set; } = new();
        public List<AaColourRowDto> ColourRows { get; set; } = new();
    }
}

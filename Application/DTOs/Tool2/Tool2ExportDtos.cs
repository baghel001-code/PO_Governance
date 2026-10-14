using System.Text.Json.Serialization;

namespace Application.DTOs.Tool2
{
    /// <summary>POST api/v1/assessment/export.</summary>
    public sealed class Tool2ExportRequestDto
    {
        /// <summary>The saved run to export (required).</summary>
        [JsonPropertyName("runId")] public string RunId { get; set; } = "";
        /// <summary>"xlsx" (default) or "csv".</summary>
        [JsonPropertyName("format")] public string Format { get; set; } = "xlsx";
        /// <summary>Category Head display names to scope the workbook to (empty = everyone, v1.1.4 default).</summary>
        [JsonPropertyName("catHeads")] public List<string>? CatHeads { get; set; }
        /// <summary>
        /// With several Category Heads selected: true → one workbook per Category Head, zipped;
        /// false → one workbook scoped to all of them (v1.1.4 behaviour). Default true.
        /// </summary>
        [JsonPropertyName("separateFiles")] public bool SeparateFiles { get; set; } = true;
    }

    /// <summary>A finished download.</summary>
    public sealed class Tool2FileDto
    {
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "";
        public byte[] Content { get; set; } = Array.Empty<byte>();
        /// <summary>The toast v1.1.4 showed ("Excel exported ✓ — 11 sheets …").</summary>
        public string? Message { get; set; }
    }

    /// <summary>Cell styling (the subset of xlsx-js-style the v1.1.4 export used).</summary>
    public sealed class Tool2CellStyleDto
    {
        [JsonPropertyName("fontColor")] public string? FontColor { get; set; }
        [JsonPropertyName("bold")] public bool? Bold { get; set; }
        [JsonPropertyName("italic")] public bool? Italic { get; set; }
        [JsonPropertyName("underline")] public bool? Underline { get; set; }
        [JsonPropertyName("size")] public double? Size { get; set; }
        /// <summary>Fill colour "RRGGBB".</summary>
        [JsonPropertyName("fill")] public string? Fill { get; set; }
        [JsonPropertyName("hAlign")] public string? HAlign { get; set; }
        [JsonPropertyName("vAlign")] public string? VAlign { get; set; }
        [JsonPropertyName("wrap")] public bool? Wrap { get; set; }
        [JsonPropertyName("indent")] public int? Indent { get; set; }
        /// <summary>Thin border colour on all four sides, null = none.</summary>
        [JsonPropertyName("border")] public string? Border { get; set; }
        /// <summary>Number format ("#,##0", "₹#,##0;(₹#,##0);\"-\"" …).</summary>
        [JsonPropertyName("numFmt")] public string? NumFmt { get; set; }
        /// <summary>Internal hyperlink target ("#'Summary Dashboard'!A1").</summary>
        [JsonPropertyName("link")] public string? Link { get; set; }
        [JsonPropertyName("linkTooltip")] public string? LinkTooltip { get; set; }

        public Tool2CellStyleDto Clone() => (Tool2CellStyleDto)MemberwiseClone();
    }

    public sealed class Tool2ColumnDto
    {
        [JsonPropertyName("width")] public double Width { get; set; }
        [JsonPropertyName("hidden")] public bool? Hidden { get; set; }
    }

    /// <summary>One worksheet: rows (cells: string, double or null), widths, merges, autofilter, styles.</summary>
    public sealed class Tool2SheetDto
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("rows")] public List<List<object?>> Rows { get; set; } = new();
        [JsonPropertyName("cols")] public List<Tool2ColumnDto>? Cols { get; set; }
        /// <summary>Merged ranges, "A1:H1".</summary>
        [JsonPropertyName("merges")] public List<string>? Merges { get; set; }
        [JsonPropertyName("autoFilter")] public string? AutoFilter { get; set; }
        /// <summary>Styles by cell address ("A1").</summary>
        [JsonPropertyName("styles")] public Dictionary<string, Tool2CellStyleDto> Styles { get; set; } = new();
    }

    /// <summary>The workbook of <c>exportAssXLSX</c> (Summary Dashboard, one sheet per Category Head × bucket, All Decisions).</summary>
    public sealed class Tool2WorkbookDto
    {
        [JsonPropertyName("fileName")] public string FileName { get; set; } = "";
        [JsonPropertyName("sheets")] public List<Tool2SheetDto> Sheets { get; set; } = new();
        /// <summary>The v1.1.4 toast text.</summary>
        [JsonPropertyName("message")] public string Message { get; set; } = "";
    }
}

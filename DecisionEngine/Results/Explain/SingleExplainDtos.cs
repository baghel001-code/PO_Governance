namespace DecisionEngine.Results.Explain
{
    // ─────────────────────────────────────────────────────────────────────────────
    // v1.1.6 S8 "How was this number worked out?" (legacy/v1.1.6/app/explain.js, single concept part): the side
    // panel as data (numbered steps in plain English, the result line, the step-by-step table) and the workbook
    // "this calculation" / "all lines" with live formulas. DISPLAY ONLY: every number is the tool's own number.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>A piece of a panel text; <see cref="Bold"/> = the value the page showed in bold.</summary>
    public sealed record ExplainSegDto(string Text, bool Bold = false);

    /// <summary>One numbered step of the panel.</summary>
    public sealed class ExplainStepDto
    {
        public string No { get; set; } = "";
        public List<ExplainSegDto> Segments { get; set; } = new();
    }

    /// <summary>The side panel (<c>Explain.panel</c> spec) for one single concept number.</summary>
    public sealed class SingleExplainDto
    {
        public string Kicker { get; set; } = "HOW THIS NUMBER WAS WORKED OUT";
        /// <summary>"BLU · delivery Nov 2026 · Max Permissible 4,317".</summary>
        public string Title { get; set; } = "";
        /// <summary>The group: "NON-PL · M-POLO · ₹349 · NON-BOGO".</summary>
        public string Sub { get; set; } = "";
        /// <summary>Amber note shown before the steps (a Watch List / no-step result), null when none.</summary>
        public string? WarnFirst { get; set; }
        public List<ExplainStepDto> Steps { get; set; } = new();
        /// <summary>The green result box.</summary>
        public List<ExplainSegDto> Result { get; set; } = new();
        /// <summary>Amber note after the result ("The table shows … the steps give …"), null when none.</summary>
        public string? Warn { get; set; }
        /// <summary>"Where the 15.9% comes from" and its text; null when not shown.</summary>
        public string? MixTitle { get; set; }
        public string? MixText { get; set; }
        /// <summary>"Step-by-step table": Month, Open, POs in, Plan sale, Close (pre-roll months, then the chain to the window end).</summary>
        public List<string>? TableHead { get; set; }
        public List<List<string>>? TableRows { get; set; }
        /// <summary>"Download this calculation (Excel)" (null = no download for this panel).</summary>
        public ExplainBookDto? Book { get; set; }
    }

    // ── workbook model (Explain.book): rows 1–3 title / who-when / blank, row 4 header, data from row 5 ──

    public sealed class ExplainBookDto
    {
        public string FileName { get; set; } = "";
        public List<ExplainSheetDto> Sheets { get; set; } = new();
    }

    public sealed class ExplainSheetDto
    {
        /// <summary>Sheet name (31 characters, no \ / ? * [ ] :).</summary>
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Subtitle { get; set; }
        public string? Note { get; set; }
        public List<ExplainColDto> Cols { get; set; } = new();
        public List<ExplainRowDto> Rows { get; set; } = new();
    }

    public sealed class ExplainColDto
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public double Width { get; set; } = 12;
        /// <summary>int, d1, d2, d4, pct, pct1, date, txt or null.</summary>
        public string? Fmt { get; set; }
    }

    public sealed class ExplainRowDto
    {
        public string? Id { get; set; }
        /// <summary>null, "head" or "total".</summary>
        public string? Style { get; set; }
        /// <summary>One cell per column (null = empty).</summary>
        public List<ExplainCellDto?> Cells { get; set; } = new();
    }

    public sealed class ExplainCellDto
    {
        /// <summary>The value (double or string); for a formula cell the tool's own value (cached result).</summary>
        public object? Value { get; set; }
        /// <summary>A1 formula without "=" (references already resolved), null for a plain value.</summary>
        public string? Formula { get; set; }
        /// <summary>"in" (blue: data from the files / settings), "link" (green: another sheet), "bold" or null (black: formula).</summary>
        public string? Style { get; set; }
        public string? Fmt { get; set; }
    }
}

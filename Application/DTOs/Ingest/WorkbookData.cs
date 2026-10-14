namespace Application.DTOs.Ingest
{
    /// <summary>
    /// A parsed workbook as SheetJS's <c>XLSX.read(buf, {type:'array', cellDates:false})</c> exposed it
    /// to the page: sheet names in workbook order and, per sheet, the used range (<c>!ref</c>) and the
    /// typed cells with their number format. Produced by <see cref="Application.Interfaces.V1.Ingest.IWorkbookReader"/>.
    /// </summary>
    public sealed class WorkbookData
    {
        public List<string> SheetNames { get; } = new();
        public Dictionary<string, SheetData> Sheets { get; } = new(StringComparer.Ordinal);

        public void Add(SheetData sheet)
        {
            if (!Sheets.ContainsKey(sheet.Name)) SheetNames.Add(sheet.Name);
            Sheets[sheet.Name] = sheet;
        }
    }

    /// <summary>Zero-based inclusive cell range (SheetJS <c>decode_range(ws['!ref'])</c>).</summary>
    public readonly record struct SheetRange(int StartRow, int StartCol, int EndRow, int EndCol);

    /// <summary>One worksheet. Cells are addressed by absolute zero-based row / column (A1 = 0,0).</summary>
    public sealed class SheetData
    {
        private readonly Dictionary<int, SheetCell?[]> _rows = new();

        public SheetData(string name) => Name = name;

        public string Name { get; }

        /// <summary>The sheet's <c>!ref</c>; null when SheetJS had none (empty sheet → sheet_to_json returns []).</summary>
        public SheetRange? Range { get; set; }

        public IEnumerable<KeyValuePair<int, SheetCell?[]>> Rows => _rows;

        public SheetCell? Get(int r, int c) =>
            _rows.TryGetValue(r, out var row) && c >= 0 && c < row.Length ? row[c] : null;

        public void Set(int r, int c, SheetCell? cell)
        {
            if (!_rows.TryGetValue(r, out var row))
            {
                row = new SheetCell?[Math.Max(c + 1, 8)];
                _rows[r] = row;
            }
            else if (c >= row.Length)
            {
                Array.Resize(ref row, Math.Max(c + 1, row.Length * 2));
                _rows[r] = row;
            }
            row[c] = cell;
        }

        /// <summary>The range spanned by the stored cells (SheetJS "refguess"), or null when there are none.</summary>
        public SheetRange? ComputeUsedRange()
        {
            int sr = int.MaxValue, sc = int.MaxValue, er = -1, ec = -1;
            foreach (var (r, row) in _rows)
            {
                for (var c = 0; c < row.Length; c++)
                {
                    if (row[c] == null) continue;
                    if (r < sr) sr = r;
                    if (r > er) er = r;
                    if (c < sc) sc = c;
                    if (c > ec) ec = c;
                }
            }
            return er < 0 ? null : new SheetRange(sr, sc, er, ec);
        }
    }

    /// <summary>SheetJS cell type (<c>cell.t</c>): n, s, b, e.</summary>
    public enum SheetCellType { Number, String, Boolean, Error }

    /// <summary>
    /// One cell: its typed value, the number-format code SheetJS would apply (<c>table_fmt[numFmtId]</c>)
    /// and, when the parser already fixed the display text (CSV), that text (<c>cell.w</c>).
    /// </summary>
    public sealed class SheetCell
    {
        public SheetCellType Type { get; init; }
        public double Number { get; init; }
        public string? Text { get; init; }
        public bool Bool { get; init; }
        /// <summary>SheetJS error code (BErr): 0x00 #NULL!, 0x07 #DIV/0!, 0x0F #VALUE!, 0x17 #REF!, 0x1D #NAME?, 0x24 #NUM!, 0x2A #N/A; -1 unknown.</summary>
        public int ErrorCode { get; init; } = -1;
        /// <summary>A numeric cell without a value (a CSV formula: <c>{t:'n', f:…}</c> with v undefined).</summary>
        public bool NoValue { get; init; }
        /// <summary>Number-format code ("General" when none).</summary>
        public string Format { get; init; } = "General";
        /// <summary>Pre-computed display text (SheetJS <c>cell.w</c>) — set by the CSV parser.</summary>
        public string? W { get; set; }
        /// <summary>.xls cells: when formatting fails SheetJS falls back to <c>''+v</c> instead of General.</summary>
        public bool BiffFallback { get; init; }

        /// <summary>The raw value as a .NET object (double / string / bool), null for errors / no value.</summary>
        public object? Value => Type switch
        {
            SheetCellType.Number => NoValue ? null : Number,
            SheetCellType.String => Text ?? "",
            SheetCellType.Boolean => Bool,
            _ => null
        };

        public static SheetCell Num(double v, string format = "General") => new() { Type = SheetCellType.Number, Number = v, Format = format };
        public static SheetCell Str(string v, string format = "General") => new() { Type = SheetCellType.String, Text = v, Format = format };
        public static SheetCell Boolean(bool v, string format = "General") => new() { Type = SheetCellType.Boolean, Bool = v, Format = format };
        public static SheetCell Err(int code) => new() { Type = SheetCellType.Error, ErrorCode = code };
    }
}

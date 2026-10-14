using Application.DTOs.Ingest;

namespace Application.Services.V1.Ingest.SheetJs
{
    /// <summary>
    /// <c>XLSX.utils.sheet_to_json(ws, {header:1, defval:'', raw:false})</c> — the only way the page
    /// read sheets. Every row of the sheet range is returned (blank rows included, as header:1 does),
    /// each padded to the range width; a cell is its formatted text (<c>cell.w</c>), '' when empty or an
    /// error, and null only for a #NULL! error. The dateNF option the page passed has no effect with
    /// cellDates:false (format_cell only uses it for 'd' cells), so it is not modelled.
    /// </summary>
    public static class SheetJson
    {
        public static List<string?[]> ToRows(SheetData? ws)
        {
            var output = new List<string?[]>();
            if (ws?.Range is not { } r) return output;
            var width = r.EndCol - r.StartCol + 1;
            for (var R = r.StartRow; R <= r.EndRow; R++)
            {
                var row = new string?[width];
                for (var C = r.StartCol; C <= r.EndCol; C++)
                {
                    var cell = ws.Get(R, C);
                    row[C - r.StartCol] = CellValue(cell);
                }
                output.Add(row);
            }
            return output;
        }

        /// <summary>make_json_row for one cell with defval '' and raw:false.</summary>
        public static string? CellValue(SheetCell? cell)
        {
            if (cell == null) return "";
            if (cell.Type == SheetCellType.Error) return cell.ErrorCode == 0 ? null : "";
            if (cell.Type == SheetCellType.Number && cell.NoValue) return "";
            return FormatCell(cell);
        }

        /// <summary>format_cell / safe_format: the display text SheetJS computes for a cell.</summary>
        public static string FormatCell(SheetCell cell)
        {
            if (cell.W != null) return cell.W;
            var v = cell.Value;
            try
            {
                cell.W = Ssf.Format(cell.Format, v);
            }
            catch (Exception)
            {
                if (cell.BiffFallback) cell.W = v is double d ? JsNum.ToStr(d) : v is bool b ? (b ? "true" : "false") : v?.ToString() ?? "";
                else
                {
                    try { cell.W = Ssf.General(v); }
                    catch (Exception) { cell.W = v is double d2 ? JsNum.ToStr(d2) : v?.ToString() ?? ""; }
                }
            }
            return cell.W;
        }
    }
}

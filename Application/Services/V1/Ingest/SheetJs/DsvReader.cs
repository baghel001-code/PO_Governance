using System.Text;
using System.Text.RegularExpressions;
using Application.DTOs.Ingest;

namespace Application.Services.V1.Ingest.SheetJs
{
    /// <summary>
    /// SheetJS's plain-text path (PRN.to_workbook → dsv_to_sheet_str) used by <c>XLSX.read</c> for
    /// .csv / tab-delimited files: bytes are read as Latin-1 unless the file starts with a UTF-8 BOM,
    /// the separator is guessed from the first 1024 characters, quoted fields are unquoted, and each
    /// field becomes a number (display text = the field as written), a boolean, a date (re-displayed as
    /// <c>m/d/yy</c>) or text. One sheet, named "Sheet1".
    /// </summary>
    public static class DsvReader
    {
        public static WorkbookData Read(byte[] data)
        {
            string str;
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
                str = Encoding.Unicode.GetString(data, 2, data.Length - 2);
            else if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                str = Encoding.UTF8.GetString(data, 3, data.Length - 3);
            else
                str = Encoding.Latin1.GetString(data);
            var wb = new WorkbookData();
            wb.Add(ParseDsv(str, "Sheet1"));
            return wb;
        }

        private static readonly Dictionary<char, int> SepWeights = new() { [','] = 3, ['\t'] = 2, [';'] = 1, ['|'] = 0 };

        /// <summary>guess_sep.</summary>
        public static char GuessSep(string str)
        {
            var cnt = new Dictionary<char, int>();
            var order = new List<char>();
            var instr = false;
            foreach (var cc in str)
            {
                if (cc == '"') instr = !instr;
                else if (!instr && SepWeights.ContainsKey(cc))
                {
                    if (!cnt.ContainsKey(cc)) { cnt[cc] = 0; order.Add(cc); }
                    cnt[cc]++;
                }
            }
            var list = new List<(int Count, char Sep)>();
            // for(end in cnt): integer-like keys (char codes) enumerate in ascending numeric order.
            if (cnt.Count == 0) foreach (var kv in SepWeights.OrderBy(k => (int)k.Key)) list.Add((kv.Value, kv.Key));
            else foreach (var k in order.OrderBy(c => (int)c)) list.Add((cnt[k], k));
            var sorted = list.ToList();
            V8Sort.Sort(sorted, (a, b) => a.Count - b.Count != 0 ? a.Count - b.Count : SepWeights[a.Sep] - SepWeights[b.Sep]);
            return sorted[^1].Sep;
        }

        /// <summary>dsv_to_sheet_str with the options XLSX.read passed (no FS, no dateNF, cellText on).</summary>
        public static SheetData ParseDsv(string str, string sheetName)
        {
            var ws = new SheetData(sheetName);
            char sep;
            if (JsNum.Slice(str, 0, 4) == "sep=")
            {
                if (JsNum.CharCodeAt(str, 5) == 13 && JsNum.CharCodeAt(str, 6) == 10) { sep = str[4]; str = JsNum.Slice(str, 7); }
                else if (JsNum.CharCodeAt(str, 5) == 13 || JsNum.CharCodeAt(str, 5) == 10) { sep = str[4]; str = JsNum.Slice(str, 6); }
                else sep = GuessSep(JsNum.Slice(str, 0, 1024));
            }
            else sep = GuessSep(JsNum.Slice(str, 0, 1024));

            int R = 0, C = 0;
            int start = 0, end = 0;
            var sepcc = (int)sep;
            var instr = false;
            int cc = 0;
            var startcc = JsNum.CharCodeAt(str, 0);
            str = str.Replace("\r\n", "\n");
            int maxC = 0, maxR = 0;

            void FinishCell()
            {
                var s = str.Substring(start, Math.Max(0, Math.Min(end, str.Length) - start));
                SheetCell? cell = null;
                if (s.Length > 0 && s[0] == '"' && s[^1] == '"') s = JsNum.Slice(s, 1, -1).Replace("\"\"", "\"");
                if (s.Length == 0) cell = null; // 'z'
                else if (JsNum.Trim(s).Length == 0) cell = SheetCell.Str(s);
                else if (s[0] == '=')
                {
                    if (JsNum.CharCodeAt(s, 1) == 0x22 && s[^1] == '"') cell = SheetCell.Str(JsNum.Slice(s, 2, -1).Replace("\"\"", "\""));
                    else if (s.Length != 1) cell = new SheetCell { Type = SheetCellType.Number, NoValue = true };
                    else cell = SheetCell.Str(s);
                }
                else if (s == "TRUE") cell = SheetCell.Boolean(true);
                else if (s == "FALSE") cell = SheetCell.Boolean(false);
                else if (!double.IsNaN(FuzzyNum(s))) cell = new SheetCell { Type = SheetCellType.Number, Number = FuzzyNum(s), W = s };
                else if (FuzzyDate(s) is { } d)
                {
                    var serial = d.WallSerial + JsDate.DatenumSkewDays;
                    string w;
                    try { w = Ssf.Format("m/d/yy", serial); } catch (Ssf.SsfException) { w = JsNum.ToStr(serial); }
                    cell = new SheetCell { Type = SheetCellType.Number, Number = serial, W = w };
                }
                else cell = SheetCell.Str(s);
                if (cell != null) ws.Set(R, C, cell);
                start = end + 1; startcc = JsNum.CharCodeAt(str, start);
                if (maxC < C) maxC = C;
                if (maxR < R) maxR = R;
                if (cc == sepcc) ++C; else { C = 0; ++R; }
            }

            for (; end < str.Length; ++end)
            {
                cc = str[end];
                if (cc == 0x22) { if (startcc == 0x22) instr = !instr; }
                else if (cc == sepcc || cc == 0x0a || cc == 0x0d) { if (!instr) FinishCell(); }
            }
            if (end - start > 0) FinishCell();
            ws.Range = new SheetRange(0, 0, maxR, maxC);
            return ws;
        }

        private static readonly Regex HasDigit = new("[0-9]", RegexOptions.CultureInvariant);
        private static readonly Regex DigitComma = new("([0-9]),([0-9])", RegexOptions.CultureInvariant);
        private static readonly Regex Parens = new(@"[(](.*)[)]", RegexOptions.CultureInvariant);

        /// <summary>SheetJS fuzzynum.</summary>
        public static double FuzzyNum(string s)
        {
            var v = JsNum.ToNumber(s);
            if (!double.IsNaN(v)) return double.IsFinite(v) ? v : double.NaN;
            if (!HasDigit.IsMatch(s)) return v;
            double wt = 1;
            var ss = DigitComma.Replace(s, m => m.Groups[1].Value + m.Groups[2].Value).Replace("$", "");
            ss = Regex.Replace(ss, "%", _ => { wt *= 100; return ""; });
            if (!double.IsNaN(v = JsNum.ToNumber(ss))) return v / wt;
            ss = Parens.Replace(ss, m => { wt = -wt; return m.Groups[1].Value; }, 1);
            if (!double.IsNaN(v = JsNum.ToNumber(ss))) return v / wt;
            return v;
        }

        private static readonly string[] LowerMonths = { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };
        private static readonly Regex MonthAbbr = new("jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec", RegexOptions.CultureInvariant);
        private static readonly Regex NonAlpha = new("[^a-z]", RegexOptions.CultureInvariant);
        private static readonly Regex AmPm = new("([^a-z]|^)[ap]m?([^a-z]|$)", RegexOptions.CultureInvariant);
        private static readonly Regex AnyAlpha = new("[a-z]", RegexOptions.CultureInvariant);
        private static readonly Regex NotDateChars = new(@"[^-0-9:,\/\\]", RegexOptions.CultureInvariant);

        /// <summary>SheetJS fuzzydate: the parsed local date, or null for an Invalid Date.</summary>
        public static JsDate.Local? FuzzyDate(string s)
        {
            var o = JsDate.Parse(s);
            if (o is not { } d) return null;
            var y = d.Year - 1900;
            var m = d.Month - 1;
            var day = d.Day;
            var lower = s.ToLowerInvariant();
            if (MonthAbbr.IsMatch(lower))
            {
                lower = NonAlpha.Replace(lower, "");
                lower = AmPm.Replace(lower, "", 1);
                if (lower.Length > 3 && Array.IndexOf(LowerMonths, lower) == -1) return null;
            }
            else if (AnyAlpha.IsMatch(lower)) return null;
            if (y < 0 || y > 8099) return null;
            if ((m > 0 || day > 1) && y != 101) return d;
            if (NotDateChars.IsMatch(s)) return null;
            return d;
        }
    }
}

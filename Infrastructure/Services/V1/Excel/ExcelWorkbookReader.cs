using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Application.DTOs.Ingest;
using Application.Interfaces.V1.Ingest;
using Application.Services.V1.Ingest.SheetJs;
using ExcelDataReader;

namespace Infrastructure.Services.V1.Excel
{
    /// <summary>
    /// <see cref="IWorkbookReader"/> on ExcelDataReader (.xlsx / .xlsm / .xlsb / .xls) plus a port of
    /// SheetJS's plain-text reader for .csv / delimited text. Like <c>XLSX.read</c> the format is chosen
    /// from the content (zip → OOXML, OLE → BIFF, anything else → delimited text).
    /// For .xlsx the sheet's <c>&lt;dimension&gt;</c> and the workbook's custom number formats are read
    /// from the package directly, because SheetJS used them for <c>!ref</c> and the cell display text.
    /// </summary>
    public sealed class ExcelWorkbookReader : IWorkbookReader
    {
        static ExcelWorkbookReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        public WorkbookData Read(byte[] content, string fileName)
        {
            if (content.Length >= 4 && content[0] == 0x50 && content[1] == 0x4B && content[2] < 0x09 && content[3] < 0x09)
                return ReadOpenXml(content);
            if (content.Length >= 8 && content[0] == 0xD0 && content[1] == 0xCF && content[2] == 0x11 && content[3] == 0xE0)
                return ReadExcel(content, null, null, biff: true);
            var first = FirstNonBlank(content);
            if (first == '<')
                throw new NotSupportedException("Unsupported file: HTML / XML spreadsheets are not supported — save it as .xlsx and upload again");
            return DsvReader.Read(content);
        }

        private static char FirstNonBlank(byte[] b)
        {
            var i = 0;
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) i = 3;
            for (; i < b.Length; i++) if (b[i] != 0x0A && b[i] != 0x0D && b[i] != 0x20) return (char)b[i];
            return '\0';
        }

        // ── OOXML side-car (dimension + numFmts) ───────────────────────

        private sealed class XlsxInfo
        {
            public Dictionary<string, SheetRange?> Dimensions { get; } = new(StringComparer.Ordinal);
            public Dictionary<int, string> NumFmts { get; } = new();
        }

        private WorkbookData ReadOpenXml(byte[] content)
        {
            XlsxInfo? info = null;
            using (var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read))
            {
                if (zip.GetEntry("xl/workbook.xml") != null || FindEntry(zip, "workbook.xml") != null)
                    info = ReadXlsxInfo(zip);
            }
            return ReadExcel(content, info?.Dimensions, info?.NumFmts, biff: false);
        }

        private static ZipArchiveEntry? FindEntry(ZipArchive zip, string suffix) =>
            zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && !e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase));

        private static readonly Regex DimRegex = new("\"(\\w*:\\w*)\"", RegexOptions.CultureInvariant);

        private static XlsxInfo ReadXlsxInfo(ZipArchive zip)
        {
            var info = new XlsxInfo();
            var wbEntry = zip.GetEntry("xl/workbook.xml") ?? FindEntry(zip, "workbook.xml")!;
            var wbDir = wbEntry.FullName.Contains('/') ? wbEntry.FullName[..(wbEntry.FullName.LastIndexOf('/') + 1)] : "";
            var relsEntry = zip.GetEntry(wbDir + "_rels/" + wbEntry.Name + ".rels");
            var rels = new Dictionary<string, string>(StringComparer.Ordinal);
            if (relsEntry != null)
            {
                foreach (var el in XmlElements(relsEntry, "Relationship"))
                {
                    if (el.TryGetValue("Id", out var id) && el.TryGetValue("Target", out var target)) rels[id] = target;
                }
            }
            foreach (var el in XmlElements(wbEntry, "sheet"))
            {
                if (!el.TryGetValue("name", out var name)) continue;
                var rid = el.FirstOrDefault(kv => kv.Key.EndsWith(":id", StringComparison.Ordinal) || kv.Key == "id").Value;
                if (rid == null || !rels.TryGetValue(rid, out var target)) continue;
                var path = target.StartsWith('/') ? target.TrimStart('/') : NormalizePath(wbDir + target);
                var entry = zip.GetEntry(path);
                info.Dimensions[name] = entry == null ? null : ReadDimension(entry);
            }
            var styles = zip.GetEntry(wbDir + "styles.xml");
            if (styles != null)
            {
                foreach (var el in XmlElements(styles, "numFmt"))
                {
                    if (el.TryGetValue("numFmtId", out var ids) && int.TryParse(ids, out var id) && el.TryGetValue("formatCode", out var code) && id > 0)
                        info.NumFmts[id] = code;
                }
            }
            return info;
        }

        private static string NormalizePath(string p)
        {
            var parts = new List<string>();
            foreach (var seg in p.Split('/'))
            {
                if (seg == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
                else if (seg != "." && seg.Length > 0) parts.Add(seg);
            }
            return string.Join('/', parts);
        }

        private static IEnumerable<Dictionary<string, string>> XmlElements(ZipArchiveEntry entry, string localName)
        {
            using var s = entry.Open();
            using var xr = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true });
            var list = new List<Dictionary<string, string>>();
            while (xr.Read())
            {
                if (xr.NodeType != XmlNodeType.Element || xr.LocalName != localName) continue;
                var d = new Dictionary<string, string>(StringComparer.Ordinal);
                if (xr.HasAttributes)
                {
                    while (xr.MoveToNextAttribute()) d[xr.Name.Contains(':') && !xr.Name.StartsWith("xmlns", StringComparison.Ordinal) ? xr.Name : xr.LocalName] = xr.Value;
                    xr.MoveToElement();
                }
                list.Add(d);
            }
            return list;
        }

        /// <summary>parse_ws_xml: the first 50 chars of the &lt;dimension&gt; tag must hold "X:Y" (a single cell ref is ignored).</summary>
        private static SheetRange? ReadDimension(ZipArchiveEntry entry)
        {
            using var s = entry.Open();
            using var sr = new StreamReader(s, Encoding.UTF8);
            var buf = new char[8192];
            var sb = new StringBuilder();
            int n;
            while ((n = sr.Read(buf, 0, buf.Length)) > 0 && sb.Length < 1 << 20)
            {
                sb.Append(buf, 0, n);
                var text = sb.ToString();
                var sd = Regex.Match(text, @"<(?:\w+:)?sheetData[^>]*>");
                if (sd.Success || n < buf.Length)
                {
                    var head = sd.Success ? text[..sd.Index] : text;
                    var dm = Regex.Match(head, @"<(?:\w*:)?dimension");
                    if (!dm.Success || dm.Index == 0) return null;
                    var slice = head.Substring(dm.Index, Math.Min(50, head.Length - dm.Index));
                    var m = DimRegex.Match(slice);
                    if (!m.Success) return null;
                    return DecodeRange(m.Groups[1].Value);
                }
            }
            return null;
        }

        private static SheetRange? DecodeRange(string r)
        {
            var parts = r.Split(':');
            if (parts.Length != 2) return null;
            var a = DecodeCell(parts[0]);
            var b = DecodeCell(parts[1]);
            if (a == null || b == null) return null;
            var d = new SheetRange(a.Value.R, a.Value.C, b.Value.R, b.Value.C);
            if (d.StartRow <= d.EndRow && d.StartCol <= d.EndCol && d.StartRow >= 0 && d.StartCol >= 0) return d;
            return null;
        }

        private static (int R, int C)? DecodeCell(string s)
        {
            int i = 0, c = 0, r = 0;
            s = s.Replace("$", "");
            while (i < s.Length && char.IsAsciiLetter(s[i])) { c = c * 26 + (char.ToUpperInvariant(s[i]) - 'A' + 1); i++; }
            var digits = s[i..];
            if (c == 0 || digits.Length == 0 || !int.TryParse(digits, out r)) return null;
            return (r - 1, c - 1);
        }

        // ── ExcelDataReader ────────────────────────────────────────────

        private static WorkbookData ReadExcel(byte[] content, Dictionary<string, SheetRange?>? dims, Dictionary<int, string>? numFmts, bool biff)
        {
            var wb = new WorkbookData();
            using var ms = new MemoryStream(content);
            using var reader = ExcelReaderFactory.CreateReader(ms, new ExcelReaderConfiguration
            {
                FallbackEncoding = Encoding.GetEncoding(1252),
                LeaveOpen = true
            });
            var isXlsx = dims != null;
            var fmtCache = new Dictionary<int, string>();
            do
            {
                var sheet = new SheetData(reader.Name);
                var r = 0;
                while (reader.Read())
                {
                    for (var c = 0; c < reader.FieldCount; c++)
                    {
                        var cell = ReadCell(reader, c, isXlsx, numFmts, biff, fmtCache);
                        if (cell != null) sheet.Set(r, c, cell);
                    }
                    r++;
                }
                if (dims != null && dims.TryGetValue(sheet.Name, out var dim) && dim != null) sheet.Range = dim;
                else sheet.Range = sheet.ComputeUsedRange();
                wb.Add(sheet);
            } while (reader.NextResult());
            return wb;
        }

        private static SheetCell? ReadCell(IExcelDataReader reader, int c, bool isXlsx, Dictionary<int, string>? numFmts, bool biff, Dictionary<int, string> fmtCache)
        {
            var v = reader.GetValue(c);
            if (v == null)
            {
                var err = reader.GetCellError(c);
                return err == null ? null : SheetCell.Err(ErrorCode(err.Value));
            }
            var fmt = Format(reader, c, isXlsx, numFmts, fmtCache);
            return v switch
            {
                double d => new SheetCell { Type = SheetCellType.Number, Number = d, Format = fmt, BiffFallback = biff },
                DateTime dt => new SheetCell { Type = SheetCellType.Number, Number = dt.ToOADate(), Format = fmt, BiffFallback = biff },
                TimeSpan ts => new SheetCell { Type = SheetCellType.Number, Number = ts.TotalDays, Format = fmt, BiffFallback = biff },
                bool b => new SheetCell { Type = SheetCellType.Boolean, Bool = b, Format = fmt, BiffFallback = biff },
                string s => new SheetCell { Type = SheetCellType.String, Text = s, Format = fmt, BiffFallback = biff },
                int i => new SheetCell { Type = SheetCellType.Number, Number = i, Format = fmt, BiffFallback = biff },
                long l => new SheetCell { Type = SheetCellType.Number, Number = l, Format = fmt, BiffFallback = biff },
                float f => new SheetCell { Type = SheetCellType.Number, Number = f, Format = fmt, BiffFallback = biff },
                decimal m => new SheetCell { Type = SheetCellType.Number, Number = (double)m, Format = fmt, BiffFallback = biff },
                _ => new SheetCell { Type = SheetCellType.String, Text = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "", Format = fmt, BiffFallback = biff }
            };
        }

        private static string Format(IExcelDataReader reader, int c, bool isXlsx, Dictionary<int, string>? numFmts, Dictionary<int, string> cache)
        {
            int id;
            try { id = reader.GetNumberFormatIndex(c); } catch { return "General"; }
            if (id <= 0) return "General";
            if (cache.TryGetValue(id, out var f)) return f;
            if (isXlsx) f = Ssf.ResolveFormat(id, numFmts);
            else if (Ssf.TableFmt.TryGetValue(id, out var t)) f = t;
            else
            {
                string? s = null;
                try { s = reader.GetNumberFormatString(c); } catch { /* ignore */ }
                f = string.IsNullOrEmpty(s) ? Ssf.ResolveFormat(id, null) : s;
            }
            cache[id] = f;
            return f;
        }

        /// <summary>ExcelDataReader CellError → SheetJS error code (BErr).</summary>
        private static int ErrorCode(CellError e) => e.ToString().ToUpperInvariant() switch
        {
            "NULL" => 0x00,
            "DIV0" => 0x07,
            "VALUE" => 0x0F,
            "REF" => 0x17,
            "NAME" => 0x1D,
            "NUM" => 0x24,
            "NA" => 0x2A,
            "GETTING_DATA" => 0x2B,
            _ => -1
        };
    }
}

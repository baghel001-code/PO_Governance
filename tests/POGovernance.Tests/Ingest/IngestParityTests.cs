using System.Globalization;
using System.Text;
using System.Text.Json;
using Application.DTOs.Ingest;
using Application.Services.V1.Ingest;
using Application.Services.V1.Ingest.SheetJs;
using Domain.Entities.Common;
using Infrastructure.Services.V1.Excel;

namespace POGovernance.Tests.Ingest
{
    /// <summary>
    /// Parity with the v1.1.4 browser code. The expected JSON was produced by
    /// tests/parity/ingest/run-parity.js, which runs the original vendor SheetJS and the functions
    /// extracted verbatim from legacy/python-app/app/index.html on the same fixture files.
    /// </summary>
    public class IngestParityTests
    {
        private static readonly ExcelWorkbookReader Reader = new();

        private static JsonElement Load(string name) =>
            JsonDocument.Parse(File.ReadAllBytes(Path.Combine(IngestTestPaths.Expected, name))).RootElement;

        /// <summary>Canonical text of a JSON value (key order kept, numbers compared as doubles).</summary>
        internal static string Canon(JsonElement e)
        {
            var sb = new StringBuilder();
            void W(JsonElement x)
            {
                switch (x.ValueKind)
                {
                    case JsonValueKind.Object:
                        sb.Append('{');
                        var first = true;
                        foreach (var p in x.EnumerateObject())
                        {
                            if (!first) sb.Append(',');
                            first = false;
                            sb.Append(JsonSerializer.Serialize(p.Name)).Append(':');
                            W(p.Value);
                        }
                        sb.Append('}');
                        break;
                    case JsonValueKind.Array:
                        sb.Append('[');
                        var f2 = true;
                        foreach (var a in x.EnumerateArray()) { if (!f2) sb.Append(','); f2 = false; W(a); }
                        sb.Append(']');
                        break;
                    case JsonValueKind.Number: sb.Append(x.GetDouble().ToString("R", CultureInfo.InvariantCulture)); break;
                    case JsonValueKind.String: sb.Append(JsonSerializer.Serialize(x.GetString())); break;
                    default: sb.Append(x.GetRawText()); break;
                }
            }
            W(e);
            return sb.ToString();
        }

        private static string Canon(byte[] json) => Canon(JsonDocument.Parse(json).RootElement);

        private static string CanonRows(List<string?[]> rows) => Canon(JsonSerializer.SerializeToUtf8Bytes(rows));

        [Fact]
        public void Ssf_format_matches_sheetjs()
        {
            var bad = new List<string>();
            foreach (var c in Load("ssf.json").EnumerateArray())
            {
                var fmt = c[0].GetString()!;
                object? v = c[1].ValueKind switch
                {
                    JsonValueKind.Number => c[1].GetDouble(),
                    JsonValueKind.String => c[1].GetString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null
                };
                var expected = c[2].ValueKind == JsonValueKind.Null ? null : c[2].GetString();
                string? actual;
                try { actual = Ssf.Format(fmt, v); } catch (Exception) { actual = null; }
                if (actual != expected) bad.Add($"fmt={fmt} v={(v is double d ? d.ToString("R", CultureInfo.InvariantCulture) : v)} expected={expected ?? "<throws>"} actual={actual ?? "<throws>"}");
            }
            Assert.True(bad.Count == 0, bad.Count + " SSF mismatches:\n" + string.Join("\n", bad.Take(60)));
        }

        public static IEnumerable<object[]> SheetFiles() =>
            Load("sheets.json").EnumerateObject().Select(p => new object[] { p.Name });

        [Theory]
        [MemberData(nameof(SheetFiles))]
        public void Reader_and_sheet_to_json_match_sheetjs(string file)
        {
            var exp = Load("sheets.json").GetProperty(file);
            var bytes = File.ReadAllBytes(IngestTestPaths.Fixture(file));
            if (exp.TryGetProperty("error", out _))
            {
                Assert.ThrowsAny<Exception>(() => Reader.Read(bytes, file));
                return;
            }
            var wb = Reader.Read(bytes, file);
            Assert.Equal(exp.GetProperty("sheetNames").EnumerateArray().Select(x => x.GetString()).ToList(), wb.SheetNames);
            foreach (var sheet in exp.GetProperty("sheets").EnumerateObject())
            {
                var rows = SheetJson.ToRows(wb.Sheets[sheet.Name]);
                if (file.EndsWith(".xlsb", StringComparison.OrdinalIgnoreCase))
                {
                    // Known difference: ExcelDataReader reports only the first column of this
                    // SheetJS-written .xlsb (its FieldCount ignores the BrtWsDim A1:E4); compare column A.
                    var expA = sheet.Value.EnumerateArray().Select(r => r[0].GetString()).ToList();
                    Assert.Equal(expA, rows.Select(r => r[0]).ToList());
                    continue;
                }
                Assert.Equal(Canon(sheet.Value), CanonRows(rows));
            }
        }

        public static IEnumerable<object[]> Scenarios() =>
            Load("scenario-list.json").EnumerateArray().Select(s => new object[] { s.GetProperty("name").GetString()! });

        private static IngestUpload Upload(string name) => new(name, File.ReadAllBytes(IngestTestPaths.Fixture(name)));

        [Theory]
        [MemberData(nameof(Scenarios))]
        public void Upload_flows_match_browser(string name)
        {
            var sc = Load("scenario-list.json").EnumerateArray().First(s => s.GetProperty("name").GetString() == name);
            var exp = Load("scenarios.json").GetProperty(name);
            var svc = new DataIngestService(Reader);
            var files = sc.GetProperty("files").EnumerateArray().Select(f => Upload(f.GetString()!)).ToList();
            IngestResult res;
            switch (sc.GetProperty("kind").GetString())
            {
                case "file":
                {
                    var key = sc.GetProperty("key").GetString()!;
                    List<SheetRow>? existing = null;
                    if (sc.TryGetProperty("before", out var before))
                        existing = svc.ParseFiles(key, before.EnumerateArray().Select(f => Upload(f.GetString()!)).ToList()).Keys[0].Payload.Rows;
                    res = svc.ParseFiles(key, files, existing);
                    break;
                }
                case "zip": res = svc.ParseZip(files[0], _ => false); break;
                case "raw": res = svc.ParseRaw(files); break;
                default: res = svc.ParseGrc(files[0]); break;
            }

            var expKeys = exp.GetProperty("keys").EnumerateArray().ToList();
            Assert.Equal(expKeys.Select(k => k.GetProperty("key").GetString()), res.Keys.Select(k => k.Key));
            for (var i = 0; i < expKeys.Count; i++)
            {
                Assert.Equal(expKeys[i].GetProperty("label").GetString(), res.Keys[i].Label);
                var expPayload = Canon(expKeys[i].GetProperty("payload"));
                var actPayload = Canon(res.Keys[i].Payload.ToJson());
                if (expPayload != actPayload)
                {
                    // Show the first differing row for a readable failure.
                    var er = expKeys[i].GetProperty("payload").GetProperty("rows").EnumerateArray().Select(Canon).ToList();
                    var ar = JsonDocument.Parse(res.Keys[i].Payload.ToJson()).RootElement.GetProperty("rows").EnumerateArray().Select(Canon).ToList();
                    var j = 0;
                    while (j < Math.Min(er.Count, ar.Count) && er[j] == ar[j]) j++;
                    Assert.Fail($"{res.Keys[i].Key}: rows {er.Count} vs {ar.Count}; first difference at row {j}:\nexpected {(j < er.Count ? er[j] : "<none>")}\nactual   {(j < ar.Count ? ar[j] : "<none>")}\n(expected payload {expPayload.Length} chars, actual {actPayload.Length})");
                }
            }
            Assert.Equal(exp.GetProperty("status").GetString(), res.Report.Status);
            Assert.Equal(exp.GetProperty("statusIsError").GetBoolean(), res.Report.StatusIsError);
            var toasts = exp.GetProperty("toasts").EnumerateArray().Select(t => t.GetString()!).ToList();
            if (name == "zip_bundle" || res.Error == null) Assert.Equal(toasts, res.Report.Toasts);
            else Assert.Equal(toasts.Count, res.Report.Toasts.Count);
            Assert.Equal(exp.GetProperty("alerts").EnumerateArray().Select(t => t.GetString()), res.Report.Alerts);
            // Error texts of unreadable files come from the library (SheetJS vs .NET) and differ.
            static string Norm(string? l) => System.Text.RegularExpressions.Regex.Replace(l ?? "", @"(could not read file: |\(read error: ).*$", "$1<message>");
            Assert.Equal(exp.GetProperty("lines").EnumerateArray().Select(t => Norm(t.GetString())), res.Report.Lines.Select(Norm));
            foreach (var (k, text) in res.Report.SlotStatus)
                Assert.Equal(exp.GetProperty("slotStatus").GetProperty(k).GetString(), text);
            if (exp.GetProperty("picker").ValueKind == JsonValueKind.Array)
            {
                var picker = exp.GetProperty("picker").EnumerateArray().Select(x => x.GetString()).ToList();
                Assert.Equal(picker, res.Report.Periods);
                Assert.Equal(picker[^1], res.Report.DefaultPeriod);
            }
        }
    }
}

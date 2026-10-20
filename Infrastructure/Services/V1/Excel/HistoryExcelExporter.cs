using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Application.Interfaces.V1;
using ClosedXML.Excel;
using DecisionEngine.Common;
using DecisionEngine.Model;
using Domain.Entities.AppSettings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.V1.Excel
{
    /// <summary>
    /// History tab workbooks (history.js exportRequests / downloadUpload / downloadVersion, SheetJS
    /// json_to_sheet): header row = the object keys in first-seen order, one row per object, numbers as
    /// numeric cells, empty / null values left blank.
    /// </summary>
    public sealed class HistoryExcelExporter : IHistoryExcelExporter
    {
        private static readonly Regex NonAlnum = new("[^A-Za-z0-9 ]", RegexOptions.Compiled);
        private readonly TimeZoneInfo _tz;

        public HistoryExcelExporter(IOptions<PoGovSettings> cfg) => _tz = AppClock.FindZone(cfg.Value.BusinessTimeZone);

        // ── Tool 1 requests ─────────────────────────────────────────────
        public byte[] WriteRequests(IReadOnlyList<JsonObject> requests)
        {
            var rows = requests.Select(RequestRow).ToList();
            using var wb = new XLWorkbook();
            WriteSheet(wb.Worksheets.Add("Tool 1 Requests"), rows.Select(r => (IEnumerable<KeyValuePair<string, object?>>)r));
            return Save(wb);
        }

        /// <summary>history.js exportRequests row.</summary>
        private List<KeyValuePair<string, object?>> RequestRow(JsonObject r)
        {
            var i = r["info"] as JsonObject;
            var eng = Str(r, "engine");
            var cleared = r["cleared"] as JsonObject;
            var str = Num(r["artSTR"]);
            var maxPerm = r["maxPermQty"];
            return new List<KeyValuePair<string, object?>>
            {
                new("Request ID", Str(r, "reqId")),
                new("Saved At", When(Truthy(r["savedAt"]) ? Str(r, "savedAt") : Str(r, "createdAt"))),
                new("Requested By", Str(r, "requestedBy")),
                new("Request Date", Str(r, "reqDate")),
                new("Generic Material", Val(r["gm"])),
                new("Colour", Val(r["colour"])),
                new("Brand", Or(i?["brand"])),
                new("Division", Or(i?["div"])),
                new("MC CD", Or(i?["mcCd"])),
                new("MC Desc", Or(i?["mcDesc"])),
                new("MRP", Or(i?["mrp"])),
                new("Flag", Or(i?["flag"])),
                new("Delivery Month", Val(r["month"])),
                new("Proposed Qty", Val(r["qty"])),
                new("Engine", eng == "B" ? "Single Concept MRP" : eng == "A" ? "Multi Concept MRP" : "New Option"),
                new("Option Type", Or(r["optionType"])),
                new("Decision", Val(r["decision"])),
                new("Approved Qty", ApprovedQtyFor(r)),
                new("Max Permissible Qty", maxPerm is null ? "" : Val(maxPerm)),
                new("Colour STR (days)", str is { } s ? (object)Js.Round(s) : ""),
                new("Quartile", Or(r["quartile_"])),
                new("Decision Reason", Or(r["decisionReason"])),
                new("Cleared", cleared != null ? "by " + (Truthy(cleared["by"]) ? Str(cleared, "by") : "?") + " " + When(Str(cleared, "at")) : "")
            };
        }

        /// <summary>index.html _approvedQtyFor: engine B only — confirmed qty, else qty for Auto Allow, else 0.</summary>
        private static object? ApprovedQtyFor(JsonObject r)
        {
            if (Str(r, "engine") != "B") return null;
            if (r["finalApprovedQty"] is { } f) return Val(f) ?? 0d;
            if (Str(r, "decision") == "Auto Allow") return Truthy(r["qty"]) ? Val(r["qty"]) : 0d;
            return 0d;
        }

        /// <summary>history.js when(): "14 Oct 2026, 10:30 am" in the business time zone.</summary>
        private string When(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "—";
            if (!DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto)) return "Invalid Date";
            var t = TimeZoneInfo.ConvertTime(dto, _tz);
            return t.ToString("dd MMM yyyy, hh:mm ", CultureInfo.InvariantCulture) + (t.Hour < 12 ? "am" : "pm");
        }

        // ── stored data files ───────────────────────────────────────────
        public byte[] WriteDatasets(IReadOnlyList<HistoryDatasetSheet> files, bool single)
        {
            using var wb = new XLWorkbook();
            var used = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var f in files)
            {
                var label = DatasetKeys.Labels.TryGetValue(f.Key, out var l) ? l : f.Key;
                string name;
                if (single)
                {
                    name = Cut(NonAlnum.Replace(label, ""), 30);
                    if (name.Length == 0) name = "Data";
                }
                else
                {
                    name = NonAlnum.Replace(label, "").Trim();
                    name = Cut(name.Length > 0 ? name : f.Key, 28);
                    while (used.TryGetValue(name, out var n)) { used[name] = n + 1; name = Cut(name, 26) + "_" + n; }
                    used[name] = 1;
                }
                WriteSheet(wb.Worksheets.Add(name), Rows(f.Rows));
            }
            if (files.Count == 0) wb.Worksheets.Add("Data");
            return Save(wb);
        }

        private static IEnumerable<IEnumerable<KeyValuePair<string, object?>>> Rows(JsonElement rows)
        {
            if (rows.ValueKind != JsonValueKind.Array) yield break;
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                yield return row.EnumerateObject().Select(p => new KeyValuePair<string, object?>(p.Name, Val(p.Value)));
            }
        }

        // ── writer ──────────────────────────────────────────────────────
        private static void WriteSheet(IXLWorksheet ws, IEnumerable<IEnumerable<KeyValuePair<string, object?>>> rows)
        {
            var cols = new Dictionary<string, int>(StringComparer.Ordinal);
            var r = 2;
            foreach (var row in rows)
            {
                foreach (var (key, value) in row)
                {
                    if (!cols.TryGetValue(key, out var c))
                    {
                        c = cols[key] = cols.Count + 1;
                        ws.Cell(1, c).Value = key;
                    }
                    var cell = ws.Cell(r, c);
                    switch (value)
                    {
                        case null: break;
                        case double d: cell.Value = d; break;
                        case bool b: cell.Value = b; break;
                        case string s: cell.Value = s; break;
                        default: cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture); break;
                    }
                }
                r++;
            }
        }

        private static byte[] Save(XLWorkbook wb)
        {
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // ── JSON helpers (JS semantics) ─────────────────────────────────
        private static string Cut(string s, int n) => s.Length > n ? s[..n] : s;

        /// <summary>A numeric JsonValue as double, whatever CLR type backs it (int, long, decimal, JsonElement…).</summary>
        private static double D(JsonValue v) => double.Parse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);

        private static object? Val(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.Number => e.GetDouble(),
            JsonValueKind.String => e.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => e.GetRawText()
        };

        private static object? Val(JsonNode? n) => n switch
        {
            null => null,
            JsonValue v => v.GetValueKind() switch
            {
                JsonValueKind.Number => D(v),
                JsonValueKind.String => v.GetValue<string>(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            },
            _ => n.ToJsonString()
        };

        /// <summary>JS <c>x || ''</c>.</summary>
        private static object? Or(JsonNode? n) => Truthy(n) ? Val(n) : "";

        private static bool Truthy(JsonNode? n) => n is JsonValue v
            ? v.GetValueKind() switch
            {
                JsonValueKind.String => v.GetValue<string>().Length > 0,
                JsonValueKind.Number => D(v) != 0,
                JsonValueKind.True => true,
                _ => false
            }
            : n != null;

        private static string Str(JsonObject? o, string key) => o?[key] is JsonValue v
            ? v.GetValueKind() switch
            {
                JsonValueKind.String => v.GetValue<string>(),
                JsonValueKind.Number => Js.NumberToString(D(v)),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => ""
            }
            : "";

        private static double? Num(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? D(v)
            : n is JsonValue s && s.GetValueKind() == JsonValueKind.String && double.TryParse(s.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d
            : null;
    }

    public static class HistoryExcelRegistration
    {
        /// <summary>
        /// Registers <see cref="IHistoryExcelExporter"/> for the History export endpoints
        /// (POGovApi HistoryExportController). Call from POGovApi Program.cs: <c>builder.Services.AddHistoryExcelExport();</c>
        /// </summary>
        public static IServiceCollection AddHistoryExcelExport(this IServiceCollection services)
        {
            services.AddSingleton<IHistoryExcelExporter, HistoryExcelExporter>();
            return services;
        }
    }
}

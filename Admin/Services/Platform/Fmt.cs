using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DecisionEngine.Common;
using DecisionEngine.Model;

namespace Admin.Services.Platform
{
    /// <summary>
    /// Reading the loose JSON records the API returns (requests, approvals, feedback, runs, upload log lines)
    /// with the browser tool's forgiving semantics (missing / null / "" all read as empty).
    /// </summary>
    public static class J
    {
        public static JsonNode? Get(this JsonObject? o, string key) => o != null && o.TryGetPropertyValue(key, out var v) ? v : null;

        public static JsonObject? Obj(this JsonObject? o, string key) => o.Get(key) as JsonObject;

        /// <summary>A numeric JsonValue as double, whatever CLR type backs it.</summary>
        private static double D(JsonValue v) => double.Parse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);

        public static string Str(this JsonObject? o, string key) => Text(o.Get(key));

        public static string Text(JsonNode? n)
        {
            if (n is not JsonValue v) return n == null ? "" : n.ToJsonString();
            return v.GetValueKind() switch
            {
                JsonValueKind.String => v.GetValue<string>(),
                JsonValueKind.Number => Js.NumberToString(D(v)),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => ""
            };
        }

        /// <summary>Number value, or null when missing / empty / not numeric.</summary>
        public static double? Num(this JsonObject? o, string key) => Num(o.Get(key));

        public static double? Num(JsonNode? n)
        {
            if (n is not JsonValue v) return null;
            switch (v.GetValueKind())
            {
                case JsonValueKind.Number: return D(v);
                case JsonValueKind.String:
                    var s = v.GetValue<string>().Trim();
                    return s.Length > 0 && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
                case JsonValueKind.True: return 1;
                case JsonValueKind.False: return 0;
                default: return null;
            }
        }

        public static bool Truthy(this JsonObject? o, string key)
        {
            var n = o.Get(key);
            if (n is JsonValue v)
                return v.GetValueKind() switch
                {
                    JsonValueKind.String => v.GetValue<string>().Length > 0,
                    JsonValueKind.Number => D(v) != 0,
                    JsonValueKind.True => true,
                    _ => false
                };
            return n != null;
        }

        public static List<string> StrList(this JsonObject? o, string key) =>
            o.Get(key) is JsonArray a ? a.Select(Text).Where(s => s.Length > 0).ToList() : new List<string>();

        /// <summary>index.html _approvedQtyFor: only Single Concept (engine B) requests carry an approved quantity.</summary>
        public static double? ApprovedQtyFor(JsonObject r)
        {
            if (r.Str("engine") != "B") return null;
            var f = r.Get("finalApprovedQty");
            if (f != null && !(f is JsonValue fv && fv.GetValueKind() == JsonValueKind.Null)) return Num(f) ?? 0;
            if (r.Str("decision") == "Auto Allow") return r.Num("qty") ?? 0;
            return 0;
        }
    }

    /// <summary>The browser tool's display helpers (en-IN, business time zone IST).</summary>
    public static class Fmt
    {
        private static readonly TimeZoneInfo Ist = FindIst();

        private static TimeZoneInfo FindIst()
        {
            foreach (var id in new[] { "Asia/Kolkata", "India Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { /* try next */ }
            }
            return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "IST", "IST");
        }

        public static DateTime NowIst => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist).DateTime;

        /// <summary>yyyy-MM-dd of a date (JS ymd()).</summary>
        public static string Ymd(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>JS when(): toLocaleString('en-IN', {day:'2-digit', month:'short', year:'numeric', hour:'2-digit', minute:'2-digit'}) → "14 Oct 2026, 10:30 am".</summary>
        public static string When(string? iso)
        {
            if (string.IsNullOrWhiteSpace(iso)) return "—";
            DateTime local;
            var hasOffset = iso.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(iso, @"[+-]\d{2}:?\d{2}$");
            if (hasOffset && DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
                local = TimeZoneInfo.ConvertTime(dto, Ist).DateTime;
            else if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                local = dt;
            else return "Invalid Date";
            return local.ToString("dd MMM yyyy, hh:mm ", CultureInfo.InvariantCulture) + (local.Hour < 12 ? "am" : "pm");
        }

        /// <summary>JS n(): '—' for null/'' else Math.round(v).toLocaleString('en-IN').</summary>
        public static string N(double? v) => v == null ? "—" : Js.Fmt(v.Value);

        public static string N(JsonObject? o, string key) => N(o.Num(key));

        /// <summary>History mb(): "1.2 MB" / "34 KB".</summary>
        public static string Mb(double b) => b > 1048576
            ? (b / 1048576).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
            : Math.Max(1, Js.Round(b / 1024)).ToString(CultureInfo.InvariantCulture) + " KB";

        public static string Label(string key) => DatasetKeys.Labels.TryGetValue(key, out var l) ? l : key;

        /// <summary>index.html dpill() class.</summary>
        public static string DpillClass(string decision) => decision switch
        {
            "Auto Allow" or "Allow" => "dp-allow",
            "Recommend Reduced Qty" => "dp-reduce",
            "Watch List" => "dp-watch",
            "Hold — Don't Raise" => "dp-hold",
            "Block" => "dp-block",
            _ => "dp-nodata"
        };

        /// <summary>shared.js approvalBadge(): colour, background, text — null when the request needs no approval.</summary>
        public static (string Color, string Bg, string Text, string Title)? ApprovalBadge(JsonObject r)
        {
            var a = r.Obj("approval");
            if (a == null) return null;
            var by = a.Truthy("byName") ? a.Str("byName") : a.Truthy("by") ? a.Str("by") : "?";
            var names = string.Join(", ", a.StrList("approverNames"));
            var st = a.Str("status");
            var t = st switch
            {
                "pending" => ("#B45309", "#FFF7ED", "⏳ Awaiting " + (names.Length > 0 ? names : "admin")),
                "approved" => ("#00873E", "#DCFCE7", "✓ Approved by " + by + (a.Num("qty") is { } q ? " · " + Js.Fmt(q) : "")),
                "rejected" => ("#DC2626", "#FEF2F2", "✕ Rejected by " + by),
                "cleared" => ("#6B7280", "#F3F4F6", "⊘ Cleared by admin – no longer waiting"),
                _ => ("#6B7280", "#F3F4F6", st)
            };
            return (t.Item1, t.Item2, t.Item3, a.Str("comment"));
        }
    }
}

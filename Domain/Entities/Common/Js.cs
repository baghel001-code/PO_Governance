using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Domain.Entities.Common
{
    /// <summary>
    /// JavaScript-compatible helpers. The decision rules were written in browser JavaScript
    /// (legacy/python-app/app/index.html); these reproduce the exact JS semantics the rules
    /// depend on (parseFloat, Math.round, toFixed, en-IN number formatting, String(number)),
    /// so the C# engines return the same figures as the approved v1.1.4 tool.
    /// </summary>
    public static class Js
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Regex FloatPrefix = new(@"^[+-]?(Infinity|(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?)", RegexOptions.Compiled);

        /// <summary>JS parseFloat: parses the longest numeric prefix, NaN when there is none.</summary>
        public static double ParseFloat(string? s)
        {
            if (s == null) return double.NaN;
            var t = s.TrimStart(' ', '\t', '\n', '\r', '\v', '\f', ' ', '﻿');
            var m = FloatPrefix.Match(t);
            if (!m.Success) return double.NaN;
            var v = m.Value;
            if (v.EndsWith("Infinity", StringComparison.Ordinal))
                return v.StartsWith('-') ? double.NegativeInfinity : double.PositiveInfinity;
            return double.TryParse(v, NumberStyles.Float, Inv, out var d) ? d : double.NaN;
        }

        /// <summary>JS Number(str): whole string must be numeric; "" / whitespace → 0.</summary>
        public static double Number(string? s)
        {
            if (s == null) return 0;
            var t = s.Trim();
            if (t.Length == 0) return 0;
            if (t == "Infinity" || t == "+Infinity") return double.PositiveInfinity;
            if (t == "-Infinity") return double.NegativeInfinity;
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(t[2..], NumberStyles.HexNumber, Inv, out var h) ? h : double.NaN;
            return double.TryParse(t, NumberStyles.Float, Inv, out var d) ? d : double.NaN;
        }

        /// <summary>
        /// index.html <c>num(v)</c>: parseFloat after removing thousands commas; NaN → 0.
        /// </summary>
        public static double Num(object? v)
        {
            if (v == null) return 0;
            if (v is double d) return double.IsNaN(d) ? 0 : d;
            if (v is int i) return i;
            if (v is long l) return l;
            if (v is decimal m) return (double)m;
            if (v is bool) return 0; // parseFloat("true") is NaN → 0
            var n = ParseFloat(ToJsString(v).Replace(",", ""));
            return double.IsNaN(n) ? 0 : n;
        }

        /// <summary>Recommendation-engine <c>_sf(v)</c>: parseFloat(String(v||'0') without commas), non-finite → 0.</summary>
        public static double Sf(object? v)
        {
            var s = Truthy(v) ? ToJsString(v) : "0";
            var n = ParseFloat(s.Replace(",", ""));
            return double.IsFinite(n) ? n : 0;
        }

        /// <summary>Recommendation-engine <c>_n(v)</c>: String(v||'').trim(), drop a trailing ".0", lowercase.</summary>
        public static string N(object? v)
        {
            var s = (Truthy(v) ? ToJsString(v) : "").Trim();
            if (s.EndsWith(".0", StringComparison.Ordinal)) s = s[..^2];
            return s.ToLowerInvariant();
        }

        /// <summary>JS truthiness of a cell value.</summary>
        public static bool Truthy(object? v) => v switch
        {
            null => false,
            string s => s.Length > 0,
            double d => d != 0 && !double.IsNaN(d),
            int i => i != 0,
            long l => l != 0,
            bool b => b,
            _ => true
        };

        /// <summary>JS Math.round: rounds .5 towards +Infinity (Math.round(-2.5) = -2).</summary>
        public static double Round(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return x;
            return Math.Floor(x + 0.5);
        }

        /// <summary>JS Number.prototype.toFixed(digits).</summary>
        public static string ToFixed(double x, int digits)
        {
            if (double.IsNaN(x)) return "NaN";
            if (double.IsInfinity(x)) return x > 0 ? "Infinity" : "-Infinity";
            if (Math.Abs(x) >= 1e21) return NumberToString(x);
            decimal dm;
            try { dm = (decimal)x; } catch (OverflowException) { return NumberToString(x); }
            var r = Math.Round(dm, digits, MidpointRounding.AwayFromZero);
            var s = r.ToString("F" + digits, Inv);
            if (r == 0 && x < 0 && s.StartsWith("-")) s = s[1..];
            return s;
        }

        /// <summary>JS String(number).</summary>
        public static string NumberToString(double d)
        {
            if (double.IsNaN(d)) return "NaN";
            if (double.IsPositiveInfinity(d)) return "Infinity";
            if (double.IsNegativeInfinity(d)) return "-Infinity";
            if (d == 0) return "0";
            if (Math.Abs(d) < 1e21 && Math.Abs(d) >= 1e-6)
            {
                if (d == Math.Floor(d)) return d.ToString("0", Inv);
                var r = d.ToString("R", Inv);
                if (!r.Contains('E')) return r;
                return ((decimal)d).ToString(Inv);
            }
            var e = d.ToString("R", Inv).Replace("E+", "e+").Replace("E-", "e-");
            return e;
        }

        /// <summary>JS String(v) for the value types a parsed sheet row can hold.</summary>
        public static string ToJsString(object? v) => v switch
        {
            null => "",
            string s => s,
            double d => NumberToString(d),
            int i => i.ToString(Inv),
            long l => l.ToString(Inv),
            decimal m => NumberToString((double)m),
            bool b => b ? "true" : "false",
            DateTime dt => dt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Inv),
            _ => Convert.ToString(v, Inv) ?? ""
        };

        /// <summary>
        /// <c>Math.round(v).toLocaleString('en-IN')</c> — the tool's <c>fmt()</c>. Indian digit grouping (12,34,567).
        /// </summary>
        public static string Fmt(double v) => GroupIN(Round(v));

        /// <summary>Indian grouping of an already-rounded integer value.</summary>
        public static string GroupIN(double rounded)
        {
            if (double.IsNaN(rounded)) return "NaN";
            if (double.IsInfinity(rounded)) return rounded > 0 ? "∞" : "-∞";
            var neg = rounded < 0;
            var digits = Math.Abs(rounded).ToString("0", Inv);
            var sb = new StringBuilder();
            if (digits.Length <= 3) sb.Append(digits);
            else
            {
                var last3 = digits[^3..];
                var rest = digits[..^3];
                var parts = new List<string>();
                while (rest.Length > 2) { parts.Insert(0, rest[^2..]); rest = rest[..^2]; }
                if (rest.Length > 0) parts.Insert(0, rest);
                sb.Append(string.Join(",", parts)).Append(',').Append(last3);
            }
            return (neg && digits != "0" ? "-" : "") + sb;
        }

        /// <summary>
        /// <c>n.toLocaleString('en-IN', {maximumFractionDigits: maxFrac})</c> (default 3, like JS) for non-integers.
        /// </summary>
        public static string LocaleIN(double v, int maxFrac = 3)
        {
            if (double.IsNaN(v)) return "NaN";
            var rounded = Math.Round((decimal)v, maxFrac, MidpointRounding.AwayFromZero);
            var intPart = Math.Truncate(rounded);
            var frac = Math.Abs(rounded - intPart);
            var s = GroupIN((double)intPart);
            if (rounded < 0 && intPart == 0) s = "-0";
            if (frac == 0) return s;
            var f = frac.ToString("0." + new string('#', maxFrac), Inv);
            return s + f[1..];
        }

        /// <summary>The tool's <c>fmtCr</c>: Cr / L compact rupee display.</summary>
        public static string FmtCr(double v)
        {
            if (v >= 1e7) return ToFixed(v / 1e7, 2) + "Cr";
            if (v >= 1e5) return ToFixed(v / 1e5, 2) + "L";
            return Fmt(v);
        }

        /// <summary>The tool's <c>fmtD</c>: days with a "d" suffix, "—" for NaN.</summary>
        public static string FmtD(double v) => double.IsNaN(v) ? "—" : Js.NumberToString(Round(v)) + "d";

        /// <summary>JS Number.isFinite / isFinite for a number.</summary>
        public static bool IsFinite(double d) => double.IsFinite(d);

        /// <summary>Normalises a column header like index.html <c>_colNorm</c>.</summary>
        public static string ColNorm(string s)
        {
            var t = Regex.Replace(s, "[\n\r]+", " ");
            t = Regex.Replace(t, @"\s+", " ");
            return t.Trim().ToLowerInvariant();
        }
    }
}

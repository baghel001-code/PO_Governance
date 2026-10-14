using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Application.Services.V1.Ingest.SheetJs
{
    /// <summary>
    /// Exact JavaScript number / string semantics needed to reproduce what SheetJS produced in the
    /// browser: <c>Number.prototype.toString / toFixed / toPrecision / toExponential</c> (correctly
    /// rounded on the exact binary value, ties up, like V8), <c>Number(str)</c>, <c>isNaN(str)</c>,
    /// <c>String.prototype.trim</c> and JS <c>substr / slice</c>.
    /// </summary>
    public static class JsNum
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The characters JS <c>trim()</c> and regex <c>\s</c> treat as white space.</summary>
        public const string WsClass = @"\t\n\v\f\r \u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF";

        public static bool IsJsWhiteSpace(char c) => c switch
        {
            '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or '\u00A0' or '\u1680' or '\u2028' or '\u2029'
                or '\u202F' or '\u205F' or '\u3000' or '\uFEFF' => true,
            >= '\u2000' and <= '\u200A' => true,
            _ => false
        };

        /// <summary>JS <c>String.prototype.trim()</c>.</summary>
        public static string Trim(string s)
        {
            int a = 0, b = s.Length;
            while (a < b && IsJsWhiteSpace(s[a])) a++;
            while (b > a && IsJsWhiteSpace(s[b - 1])) b--;
            return a == 0 && b == s.Length ? s : s.Substring(a, b - a);
        }

        /// <summary>JS <c>(v|0) === v</c> — an integer representable as int32.</summary>
        public static bool IsInt32(double v) => v >= int.MinValue && v <= int.MaxValue && Math.Floor(v) == v;

        /// <summary>JS ToInt32 (the <c>x|0</c> operator).</summary>
        public static int ToInt32(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            var t = Math.Truncate(v);
            var m = t % 4294967296.0;
            if (m < 0) m += 4294967296.0;
            return m >= 2147483648.0 ? (int)(m - 4294967296.0) : (int)m;
        }

        // ── Number → string ────────────────────────────────────────────

        /// <summary>JS <c>String(number)</c> / <c>Number.prototype.toString(10)</c>.</summary>
        public static string ToStr(double d)
        {
            if (double.IsNaN(d)) return "NaN";
            if (double.IsPositiveInfinity(d)) return "Infinity";
            if (double.IsNegativeInfinity(d)) return "-Infinity";
            if (d == 0) return "0";
            if (d < 0) return "-" + ToStr(-d);
            // Shortest round-trip digits (.NET Core 3.0+ "R" is shortest) → JS layout rules.
            var r = d.ToString("R", Inv);
            string digits; int exp; // value = 0.digits × 10^exp  →  JS n = exp
            var ei = r.IndexOfAny(new[] { 'E', 'e' });
            var mant = ei >= 0 ? r[..ei] : r;
            var e10 = ei >= 0 ? int.Parse(r[(ei + 1)..], Inv) : 0;
            var dot = mant.IndexOf('.');
            var intPart = dot >= 0 ? mant[..dot] : mant;
            var fracPart = dot >= 0 ? mant[(dot + 1)..] : "";
            digits = (intPart + fracPart).TrimStart('0');
            var lead = (intPart + fracPart).Length - digits.Length;
            exp = intPart.Length - lead + e10;
            digits = digits.TrimEnd('0');
            if (digits.Length == 0) return "0";
            int k = digits.Length, n = exp;
            if (k <= n && n <= 21) return digits + new string('0', n - k);
            if (0 < n && n <= 21) return digits[..n] + "." + digits[n..];
            if (-6 < n && n <= 0) return "0." + new string('0', -n) + digits;
            var e = n - 1;
            var es = (e >= 0 ? "+" : "-") + Math.Abs(e).ToString(Inv);
            return k == 1 ? digits + "e" + es : digits[..1] + "." + digits[1..] + "e" + es;
        }

        /// <summary>Exact decimal expansion of a finite non-negative double: value = Digits / 10^Scale.</summary>
        private static (BigInteger Digits, int Scale) Exact(double x)
        {
            if (x == 0) return (BigInteger.Zero, 0);
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exponent = (int)((bits >> 52) & 0x7FF);
            long mantissa = bits & 0xFFFFFFFFFFFFFL;
            if (exponent == 0) exponent++; else mantissa |= 1L << 52;
            exponent -= 1075;
            if (exponent >= 0) return (new BigInteger(mantissa) << exponent, 0);
            var k = -exponent;
            return (new BigInteger(mantissa) * BigInteger.Pow(5, k), k);
        }

        private static BigInteger Pow10(int n) => BigInteger.Pow(10, n);

        /// <summary>Round Digits/10^Scale to an integer number of units of 10^-f, ties up.</summary>
        private static BigInteger RoundToScale(BigInteger digits, int scale, int f)
        {
            if (scale <= f) return digits * Pow10(f - scale);
            var div = Pow10(scale - f);
            var q = BigInteger.DivRem(digits, div, out var rem);
            if (rem * 2 >= div) q += 1;
            return q;
        }

        /// <summary>JS <c>Number.prototype.toFixed(f)</c>.</summary>
        public static string ToFixed(double x, int f)
        {
            if (double.IsNaN(x)) return "NaN";
            if (Math.Abs(x) >= 1e21 || double.IsInfinity(x)) return ToStr(x);
            var neg = x < 0;
            if (neg) x = -x;
            var (d, s) = Exact(x);
            var n = RoundToScale(d, s, f);
            var m = n.IsZero ? "0" : n.ToString(Inv);
            if (f != 0)
            {
                var k = m.Length;
                if (k <= f) { m = new string('0', f + 1 - k) + m; k = f + 1; }
                m = m[..(k - f)] + "." + m[(k - f)..];
            }
            return (neg ? "-" : "") + m;
        }

        /// <summary>Significant digits of x (x &gt; 0) rounded to p digits (ties up) and the decimal exponent e (x ≈ n × 10^(e−p+1)).</summary>
        private static (string Digits, int E) ToSignificant(double x, int p)
        {
            var (d, s) = Exact(x);
            var str = d.ToString(Inv);
            var L = str.Length;
            var e = L - 1 - s;
            BigInteger n;
            if (L > p)
            {
                var div = Pow10(L - p);
                n = BigInteger.DivRem(d, div, out var rem);
                if (rem * 2 >= div) n += 1;
            }
            else n = d * Pow10(p - L);
            var ns = n.ToString(Inv);
            if (ns.Length > p) { n /= 10; e += 1; ns = n.ToString(Inv); }
            return (ns, e);
        }

        /// <summary>JS <c>Number.prototype.toPrecision(p)</c>.</summary>
        public static string ToPrecision(double x, int p)
        {
            if (double.IsNaN(x)) return "NaN";
            if (double.IsInfinity(x)) return ToStr(x);
            var sign = "";
            if (x < 0) { sign = "-"; x = -x; }
            string m; int e;
            if (x == 0) { m = new string('0', p); e = 0; }
            else
            {
                (m, e) = ToSignificant(x, p);
                if (e < -6 || e >= p)
                {
                    var a = m[..1];
                    var rest = m[1..];
                    if (p != 1) a = a + "." + rest;
                    var c = e >= 0 ? "+" : "-";
                    return sign + a + "e" + c + Math.Abs(e).ToString(Inv);
                }
            }
            if (e == p - 1) return sign + m;
            if (e >= 0) return sign + m[..(e + 1)] + "." + m[(e + 1)..];
            return sign + "0." + new string('0', -(e + 1)) + m;
        }

        /// <summary>JS <c>Number.prototype.toExponential(f)</c>.</summary>
        public static string ToExponential(double x, int f)
        {
            if (double.IsNaN(x)) return "NaN";
            if (double.IsInfinity(x)) return ToStr(x);
            var sign = "";
            if (x < 0) { sign = "-"; x = -x; }
            string m; int e;
            if (x == 0) { m = new string('0', f + 1); e = 0; }
            else (m, e) = ToSignificant(x, f + 1);
            if (f != 0) m = m[..1] + "." + m[1..];
            return sign + m + "e" + (e >= 0 ? "+" : "-") + Math.Abs(e).ToString(Inv);
        }

        // ── String → number ────────────────────────────────────────────

        private static readonly Regex DecLiteral = new(@"^[+-]?(?:Infinity|(?:[0-9]+\.?[0-9]*|\.[0-9]+)(?:[eE][+-]?[0-9]+)?)$", RegexOptions.CultureInvariant);

        /// <summary>JS <c>Number(str)</c> (StringToNumber): NaN when the whole string is not numeric.</summary>
        public static double ToNumber(string? s)
        {
            if (s == null) return 0;
            var t = Trim(s);
            if (t.Length == 0) return 0;
            if (t.Length > 2 && t[0] == '0')
            {
                var radix = t[1] switch { 'x' or 'X' => 16, 'o' or 'O' => 8, 'b' or 'B' => 2, _ => 0 };
                if (radix != 0)
                {
                    double v = 0;
                    for (var i = 2; i < t.Length; i++)
                    {
                        var dv = HexVal(t[i]);
                        if (dv < 0 || dv >= radix) return double.NaN;
                        v = v * radix + dv;
                    }
                    return v;
                }
            }
            if (!DecLiteral.IsMatch(t)) return double.NaN;
            if (t.EndsWith("Infinity", StringComparison.Ordinal)) return t[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
            return double.Parse(t, NumberStyles.Float, Inv);
        }

        private static int HexVal(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1
        };

        /// <summary>JS global <c>isNaN(v)</c> for a string or number cell value.</summary>
        public static bool IsNaN(object? v) => v switch
        {
            null => false,                       // Number(null) = 0
            double d => double.IsNaN(d),
            bool => false,
            string s => double.IsNaN(ToNumber(s)),
            _ => true
        };

        private static readonly Regex FloatPrefix = new(@"^[+-]?(?:Infinity|(?:[0-9]+\.?[0-9]*|\.[0-9]+)(?:[eE][+-]?[0-9]+)?)", RegexOptions.CultureInvariant);

        /// <summary>JS <c>parseFloat(String(v))</c>.</summary>
        public static double ParseFloat(string? s)
        {
            if (s == null) return double.NaN;
            var t = s;
            int a = 0;
            while (a < t.Length && IsJsWhiteSpace(t[a])) a++;
            if (a > 0) t = t[a..];
            var m = FloatPrefix.Match(t);
            if (!m.Success) return double.NaN;
            var v = m.Value;
            if (v.EndsWith("Infinity", StringComparison.Ordinal)) return v[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
            // "1e" / "1e+" are not part of the literal: the regex already requires exponent digits.
            return double.Parse(v, NumberStyles.Float, Inv);
        }

        /// <summary>JS <c>parseInt(s, 10)</c>.</summary>
        public static double ParseInt(string? s)
        {
            if (s == null) return double.NaN;
            int i = 0;
            while (i < s.Length && IsJsWhiteSpace(s[i])) i++;
            var neg = false;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; i++; }
            var st = i;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            if (i == st) return double.NaN;
            var v = double.Parse(s[st..i], NumberStyles.None, Inv);
            return neg ? -v : v;
        }

        // ── String helpers with JS semantics ────────────────────────────

        /// <summary>JS <c>String.prototype.substr(start, length)</c>.</summary>
        public static string Substr(string s, double start, double? length = null)
        {
            var size = s.Length;
            var st = double.IsNaN(start) ? 0 : Math.Truncate(start);
            if (st < 0) st = Math.Max(size + st, 0);
            st = Math.Min(st, size);
            var len = length is null ? size - st : (double.IsNaN(length.Value) ? 0 : Math.Truncate(length.Value));
            len = Math.Min(Math.Max(len, 0), size - st);
            if (len <= 0) return "";
            return s.Substring((int)st, (int)len);
        }

        /// <summary>JS <c>String.prototype.slice(start, end)</c>.</summary>
        public static string Slice(string s, int start, int? end = null)
        {
            var len = s.Length;
            var from = start < 0 ? Math.Max(len + start, 0) : Math.Min(start, len);
            var e = end ?? len;
            var to = e < 0 ? Math.Max(len + e, 0) : Math.Min(e, len);
            return to > from ? s.Substring(from, to - from) : "";
        }

        /// <summary>JS <c>str.charAt(i)</c> ("" out of range).</summary>
        public static string CharAt(string s, int i) => i >= 0 && i < s.Length ? s[i].ToString() : "";

        /// <summary>JS <c>str.charCodeAt(i)</c> (-1 standing in for NaN out of range).</summary>
        public static int CharCodeAt(string s, int i) => i >= 0 && i < s.Length ? s[i] : -1;

        /// <summary>JS <c>haystack.indexOf(needle)</c> where an empty needle is found at 0.</summary>
        public static int IndexOf(string haystack, string needle) => haystack.IndexOf(needle, StringComparison.Ordinal);

        /// <summary>JS <c>fill(c, l)</c> helper from SSF.</summary>
        public static string Fill(string c, double l)
        {
            if (double.IsNaN(l) || l <= 0 || c.Length == 0) return "";
            var sb = new StringBuilder();
            while (sb.Length < l) sb.Append(c);
            return sb.ToString();
        }
    }
}

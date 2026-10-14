using System.Text;
using System.Text.RegularExpressions;

namespace Application.Services.V1.Ingest.SheetJs
{
    /// <summary>
    /// Port of SheetJS SSF (the number-format engine of xlsx 0.18.5 / xlsx-js-style 1.2.0, the
    /// <c>legacy/python-app/app/vendor/xlsx.min.js</c> the browser used). With
    /// <c>sheet_to_json(…, {raw:false})</c> every stored cell value is the text SSF produced, so this
    /// must format exactly like the original (General rules, "0.00", "#,##0", dates "m/d/yy", …).
    /// Function names follow the JS source (SSF_general_num, eval_fmt, write_num_flt …).
    /// </summary>
    public static class Ssf
    {
        /// <summary>Thrown where the JS throws; callers fall back like SheetJS's try/catch does.</summary>
        public sealed class SsfException : Exception
        {
            public SsfException(string m) : base(m) { }
        }

        // ── format tables ───────────────────────────────────────────────

        /// <summary>SSF table_fmt (built-in number formats SheetJS knows by id).</summary>
        public static readonly IReadOnlyDictionary<int, string> TableFmt = new Dictionary<int, string>
        {
            [0] = "General", [1] = "0", [2] = "0.00", [3] = "#,##0", [4] = "#,##0.00", [9] = "0%", [10] = "0.00%",
            [11] = "0.00E+00", [12] = "# ?/?", [13] = "# ??/??", [14] = "m/d/yy", [15] = "d-mmm-yy", [16] = "d-mmm",
            [17] = "mmm-yy", [18] = "h:mm AM/PM", [19] = "h:mm:ss AM/PM", [20] = "h:mm", [21] = "h:mm:ss",
            [22] = "m/d/yy h:mm", [37] = "#,##0 ;(#,##0)", [38] = "#,##0 ;[Red](#,##0)", [39] = "#,##0.00;(#,##0.00)",
            [40] = "#,##0.00;[Red](#,##0.00)", [45] = "mm:ss", [46] = "[h]:mm:ss", [47] = "mmss.0", [48] = "##0.0E+0",
            [49] = "@", [56] = "\"上午/下午 \"hh\"時\"mm\"分\"ss\"秒 \""
        };

        /// <summary>SSFImplicit — what safe_format loads for an id that is not in the table.</summary>
        public static readonly IReadOnlyDictionary<int, string> Implicit = new Dictionary<int, string>
        {
            [5] = "\"$\"#,##0_);\\(\"$\"#,##0\\)", [6] = "\"$\"#,##0_);[Red]\\(\"$\"#,##0\\)",
            [7] = "\"$\"#,##0.00_);\\(\"$\"#,##0.00\\)", [8] = "\"$\"#,##0.00_);[Red]\\(\"$\"#,##0.00\\)",
            [23] = "General", [24] = "General", [25] = "General", [26] = "General",
            [27] = "m/d/yy", [28] = "m/d/yy", [29] = "m/d/yy", [30] = "m/d/yy", [31] = "m/d/yy",
            [32] = "h:mm:ss", [33] = "h:mm:ss", [34] = "h:mm:ss", [35] = "h:mm:ss", [36] = "m/d/yy",
            [41] = "_(* #,##0_);_(* (#,##0);_(* \"-\"_);_(@_)",
            [42] = "_(\"$\"* #,##0_);_(\"$\"* (#,##0);_(\"$\"* \"-\"_);_(@_)",
            [43] = "_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)",
            [44] = "_(\"$\"* #,##0.00_);_(\"$\"* (#,##0.00);_(\"$\"* \"-\"??_);_(@_)",
            [50] = "m/d/yy", [51] = "m/d/yy", [52] = "m/d/yy", [53] = "m/d/yy", [54] = "m/d/yy",
            [55] = "m/d/yy", [56] = "m/d/yy", [57] = "m/d/yy", [58] = "m/d/yy",
            [59] = "0", [60] = "0.00", [61] = "#,##0", [62] = "#,##0.00",
            [63] = "\"$\"#,##0_);\\(\"$\"#,##0\\)", [64] = "\"$\"#,##0_);[Red]\\(\"$\"#,##0\\)",
            [65] = "\"$\"#,##0.00_);\\(\"$\"#,##0.00\\)", [66] = "\"$\"#,##0.00_);[Red]\\(\"$\"#,##0.00\\)",
            [67] = "0%", [68] = "0.00%", [69] = "# ?/?", [70] = "# ??/??", [71] = "m/d/yy", [72] = "m/d/yy",
            [73] = "d-mmm-yy", [74] = "d-mmm", [75] = "mmm-yy", [76] = "h:mm", [77] = "h:mm:ss",
            [78] = "m/d/yy h:mm", [79] = "mm:ss", [80] = "[h]:mm:ss", [81] = "mmss.0"
        };

        /// <summary>
        /// The format code SheetJS uses for a cell with number-format id <paramref name="id"/> when the
        /// workbook defines <paramref name="custom"/> codes (xlsx numFmts / xls FORMAT records).
        /// Mirrors parse_numFmts + safe_format's <c>SSF_load(SSFImplicit[fmtid] || "General", fmtid)</c>.
        /// </summary>
        public static string ResolveFormat(int id, IReadOnlyDictionary<int, string>? custom)
        {
            if (custom != null && id > 0 && custom.TryGetValue(id, out var c)) return c;
            if (TableFmt.TryGetValue(id, out var t)) return t;
            if (Implicit.TryGetValue(id, out var i)) return i;
            return "General";
        }

        private static readonly string[][] Days =
        {
            new[] { "Sun", "Sunday" }, new[] { "Mon", "Monday" }, new[] { "Tue", "Tuesday" }, new[] { "Wed", "Wednesday" },
            new[] { "Thu", "Thursday" }, new[] { "Fri", "Friday" }, new[] { "Sat", "Saturday" }
        };
        private static readonly string[][] Months =
        {
            new[] { "J", "Jan", "January" }, new[] { "F", "Feb", "February" }, new[] { "M", "Mar", "March" },
            new[] { "A", "Apr", "April" }, new[] { "M", "May", "May" }, new[] { "J", "Jun", "June" },
            new[] { "J", "Jul", "July" }, new[] { "A", "Aug", "August" }, new[] { "S", "Sep", "September" },
            new[] { "O", "Oct", "October" }, new[] { "N", "Nov", "November" }, new[] { "D", "Dec", "December" }
        };

        private const RegexOptions RO = RegexOptions.CultureInvariant;
        private static Regex R(string p) => new(p, RO);

        private static string S(double v) => JsNum.ToStr(v);
        private static string Fill(string c, double l) => JsNum.Fill(c, l);
        private static string Substr(string s, double a, double? l = null) => JsNum.Substr(s, a, l);
        private static string StrRev(string x) { var a = x.ToCharArray(); Array.Reverse(a); return new string(a); }
        private static string Pad0(string t, double d) => t.Length >= d ? t : Fill("0", d - t.Length) + t;
        private static string Pad0(double v, double d) => Pad0(S(v), d);
        private static string Pad_(double v, double d) { var t = S(v); return t.Length >= d ? t : Fill(" ", d - t.Length) + t; }
        private static string RPad_(double v, double d) { var t = S(v); return t.Length >= d ? t : t + Fill(" ", d - t.Length); }
        private static string Pad0r1(double v, double d) => Pad0(S(JsRound(v)), d);
        private static string Pad0r2(double v, double d) => Pad0(S(v), d);
        private const double P2_32 = 4294967296.0;
        private static string Pad0r(double v, double d)
        {
            if (v > P2_32 || v < -P2_32) return Pad0r1(v, d);
            var i = JsRound(v);
            return Pad0r2(i, d);
        }

        /// <summary>JS Math.round.</summary>
        public static double JsRound(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return x;
            var f = Math.Floor(x);
            return x - f >= 0.5 ? f + 1 : f;
        }

        private static double Log10Floor(double v) => Math.Floor(Math.Log(v) * 0.4342944819032518);

        public static bool IsGeneral(string s, int i = 0) =>
            s.Length >= 7 + i && string.Compare(s, i, "general", 0, 7, StringComparison.OrdinalIgnoreCase) == 0;

        // ── SSF_frac ──────────────────────────────────────────────────

        private static double[] Frac(double x, double D, bool mixed)
        {
            var sgn = x < 0 ? -1 : 1;
            var B = x * sgn;
            double P_2 = 0, P_1 = 1, P = 0;
            double Q_2 = 1, Q_1 = 0, Q = 0;
            var A = Math.Floor(B);
            while (Q_1 < D)
            {
                A = Math.Floor(B);
                P = A * P_1 + P_2;
                Q = A * Q_1 + Q_2;
                if ((B - A) < 0.00000005) break;
                B = 1 / (B - A);
                P_2 = P_1; P_1 = P;
                Q_2 = Q_1; Q_1 = Q;
            }
            if (Q > D) { if (Q_1 > D) { Q = Q_2; P = P_2; } else { Q = Q_1; P = P_1; } }
            if (!mixed) return new[] { 0, sgn * P, Q };
            var q = Math.Floor(sgn * P / Q);
            return new[] { q, sgn * P - q * Q, Q };
        }

        // ── dates ─────────────────────────────────────────────────────

        private sealed class DateCode
        {
            public double D, T, u, y, m, d, H, M, S, q;
        }

        private static DateCode? ParseDateCode(double v, bool b2 = false)
        {
            if (v > 2958465 || v < 0) return null;
            double date = JsNum.ToInt32(v), time = Math.Floor(86400 * (v - date));
            double dow;
            var o = new DateCode { D = date, T = time, u = 86400 * (v - date) - time };
            if (Math.Abs(o.u) < 1e-6) o.u = 0;
            if (o.u > 0.9999)
            {
                o.u = 0;
                if (++time == 86400) { o.T = time = 0; ++date; ++o.D; }
            }
            double[] dout;
            if (date == 60) { dout = b2 ? new double[] { 1317, 10, 29 } : new double[] { 1900, 2, 29 }; dow = 3; }
            else if (date == 0) { dout = b2 ? new double[] { 1317, 8, 29 } : new double[] { 1900, 1, 0 }; dow = 6; }
            else
            {
                if (date > 60) --date;
                var dt = new DateTime(1900, 1, 1).AddDays(date - 1);
                dout = new double[] { dt.Year, dt.Month, dt.Day };
                dow = (int)dt.DayOfWeek;
                if (date < 60) dow = (dow + 6) % 7;
                if (b2) { dout[0] -= 581; dow = (int)dt.DayOfWeek; }
            }
            o.y = dout[0]; o.m = dout[1]; o.d = dout[2];
            o.S = time % 60; time = Math.Floor(time / 60);
            o.M = time % 60; time = Math.Floor(time / 60);
            o.H = time;
            o.q = dow;
            return o;
        }

        private static string WriteDate(int type, string fmt, DateCode val, int ss0)
        {
            double outv = 0; int outl = 0; double y = val.y;
            switch (type)
            {
                case 98: // 'b' buddhist year
                case 121: // 'y'
                    if (type == 98) y = val.y + 543;
                    switch (fmt.Length)
                    {
                        case 1: case 2: outv = y % 100; outl = 2; break;
                        default: outv = y % 10000; outl = 4; break;
                    }
                    break;
                case 109: // 'm'
                    switch (fmt.Length)
                    {
                        case 1: case 2: outv = val.m; outl = fmt.Length; break;
                        case 3: return Months[(int)val.m - 1][1];
                        case 5: return Months[(int)val.m - 1][0];
                        default: return Months[(int)val.m - 1][2];
                    }
                    break;
                case 100: // 'd'
                    switch (fmt.Length)
                    {
                        case 1: case 2: outv = val.d; outl = fmt.Length; break;
                        case 3: return Days[(int)val.q][0];
                        default: return Days[(int)val.q][1];
                    }
                    break;
                case 104: // 'h'
                    switch (fmt.Length)
                    {
                        case 1: case 2: outv = 1 + (val.H + 11) % 12; outl = fmt.Length; break;
                        default: throw new SsfException("bad hour format: " + fmt);
                    }
                    break;
                case 72: // 'H'
                    switch (fmt.Length)
                    {
                        case 1: case 2: outv = val.H; outl = fmt.Length; break;
                        default: throw new SsfException("bad hour format: " + fmt);
                    }
                    break;
                case 77: // 'M'
                    switch (fmt.Length)
                    {
                        case 1: case 2: outv = val.M; outl = fmt.Length; break;
                        default: throw new SsfException("bad minute format: " + fmt);
                    }
                    break;
                case 115: // 's'
                {
                    if (fmt != "s" && fmt != "ss" && fmt != ".0" && fmt != ".00" && fmt != ".000") throw new SsfException("bad second format: " + fmt);
                    if (val.u == 0 && (fmt == "s" || fmt == "ss")) return Pad0(val.S, fmt.Length);
                    double tt;
                    if (ss0 >= 2) tt = ss0 == 3 ? 1000 : 100;
                    else tt = ss0 == 1 ? 10 : 1;
                    var ss = JsRound(tt * (val.S + val.u));
                    if (ss >= 60 * tt) ss = 0;
                    if (fmt == "s") return ss == 0 ? "0" : S(ss / tt);
                    var o = Pad0(ss, 2 + ss0);
                    if (fmt == "ss") return Substr(o, 0, 2);
                    return "." + Substr(o, 2, fmt.Length - 1);
                }
                case 90: // 'Z' absolute time
                    switch (fmt)
                    {
                        case "[h]": case "[hh]": outv = val.D * 24 + val.H; break;
                        case "[m]": case "[mm]": outv = (val.D * 24 + val.H) * 60 + val.M; break;
                        case "[s]": case "[ss]": outv = ((val.D * 24 + val.H) * 60 + val.M) * 60 + JsRound(val.S + val.u); break;
                        default: throw new SsfException("bad abstime format: " + fmt);
                    }
                    outl = fmt.Length == 3 ? 1 : 2;
                    break;
                case 101: // 'e'
                    outv = y; outl = 1; break;
            }
            return outl > 0 ? Pad0(outv, outl) : "";
        }

        // ── number writers ────────────────────────────────────────────

        private static string Commaify(string s)
        {
            const int w = 3;
            if (s.Length <= w) return s;
            int j = s.Length % w;
            var o = s.Substring(0, j);
            for (; j != s.Length; j += w) o += (o.Length > 0 ? "," : "") + Substr(s, j, w);
            return o;
        }

        private static string WriteNumPct(string type, string fmt, double val)
        {
            var sfmt = fmt.Replace("%", "");
            var mul = fmt.Length - sfmt.Length;
            return WriteNum(type, sfmt, val * Math.Pow(10, 2 * mul)) + Fill("%", mul);
        }

        private static string WriteNumCm(string type, string fmt, double val)
        {
            var idx = fmt.Length - 1;
            while (JsNum.CharCodeAt(fmt, idx - 1) == 44) --idx;
            return WriteNum(type, Substr(fmt, 0, idx), val / Math.Pow(10, 3 * (fmt.Length - idx)));
        }

        private static readonly Regex ReExpEng = R(@"^#+0.0E\+0$");
        private static readonly Regex ReMantissa = R(@"^([+-]?)(\d*)\.(\d*)[Ee]");
        private static readonly Regex ReE00 = R(@"E\+00$");
        private static readonly Regex ReShortExp = R(@"e[+-]\d$");
        private static readonly Regex ReEMinus = R(@"E\-");
        private static readonly Regex ReEPlus = R(@"e\+");

        private static string WriteNumExp(string fmt, double val, bool intVariant)
        {
            string o;
            var idx = fmt.IndexOf('E') - fmt.IndexOf('.') - 1;
            if (ReExpEng.IsMatch(fmt))
            {
                if (val == 0) return "0.0E+0";
                else if (val < 0) return "-" + WriteNumExp(fmt, -val, intVariant);
                var period = fmt.IndexOf('.'); if (period == -1) period = fmt.IndexOf('E');
                var ee = (int)(Log10Floor(val) % period);
                if (ee < 0) ee += period;
                o = JsNum.ToPrecision(val / Math.Pow(10, ee), idx + 1 + (period + ee) % period);
                var hasE = intVariant ? Regex.IsMatch(o, "[Ee]") : o.IndexOf('e') != -1;
                if (!hasE)
                {
                    var fakee = Log10Floor(val);
                    if (o.IndexOf('.') == -1) o = JsNum.CharAt(o, 0) + "." + Substr(o, 1) + "E+" + S(fakee - o.Length + ee);
                    else o += "E+" + S(fakee - ee);
                    if (!intVariant)
                    {
                        while (Substr(o, 0, 2) == "0.")
                        {
                            o = JsNum.CharAt(o, 0) + Substr(o, 2, period) + "." + Substr(o, 2 + period);
                            o = Regex.Replace(o, "^0+([1-9])", m => m.Groups[1].Value);
                            o = Regex.Replace(o, @"^0+\.", "0.");
                        }
                    }
                    o = ReplaceFirst(o, R(@"\+-"), "-");
                }
                var p = period; var e2 = ee;
                o = ReMantissa.Replace(o, m => m.Groups[1].Value + m.Groups[2].Value + Substr(m.Groups[3].Value, 0, (p + e2) % p) + "." + Substr(m.Groups[3].Value, e2) + "E", 1);
            }
            else o = JsNum.ToExponential(val, idx);
            if (ReE00.IsMatch(fmt) && ReShortExp.IsMatch(o)) o = Substr(o, 0, o.Length - 1) + "0" + JsNum.CharAt(o, o.Length - 1);
            if (ReEMinus.IsMatch(fmt) && ReEPlus.IsMatch(o)) o = ReplaceFirst(o, ReEPlus, "e");
            var ie = o.IndexOf('e');
            return ie < 0 ? o : o.Substring(0, ie) + "E" + o.Substring(ie + 1);
        }

        private static string ReplaceFirst(string s, Regex re, string literal) => re.Replace(s, _ => literal, 1);

        private static readonly Regex Frac1 = R(@"# (\?+)( ?)\/( ?)(\d+)");

        private static string WriteNumF1(Match r, double aval, string sign)
        {
            var den = JsNum.ParseInt(r.Groups[4].Value);
            var rr = JsRound(aval * den);
            var bas = Math.Floor(rr / den);
            var myn = rr - bas * den;
            var myd = den;
            return sign + (bas == 0 ? "" : S(bas)) + " " + (myn == 0
                ? Fill(" ", r.Groups[1].Length + 1 + r.Groups[4].Length)
                : Pad_(myn, r.Groups[1].Length) + r.Groups[2].Value + "/" + r.Groups[3].Value + Pad0(myd, r.Groups[4].Length));
        }

        private static string WriteNumF2(Match r, double aval, string sign) =>
            sign + (aval == 0 ? "" : S(aval)) + Fill(" ", r.Groups[1].Length + 2 + r.Groups[4].Length);

        private static readonly Regex Dec1 = R(@"^#*0*\.([0#]+)");
        private static readonly Regex CloseParen = R(@"\).*[0#]");
        private static readonly Regex Phone = R(@"\(###\) ###\\?-####");

        private static string Hashq(string str)
        {
            var sb = new StringBuilder();
            foreach (var cc in str)
            {
                switch (cc)
                {
                    case '#': break;
                    case '?': sb.Append(' '); break;
                    case '0': sb.Append('0'); break;
                    default: sb.Append(cc); break;
                }
            }
            return sb.ToString();
        }

        private static string Rnd(double val, int d) { var dd = Math.Pow(10, d); return S(JsRound(val * dd) / dd); }
        private static double Dec(double val, int d)
        {
            var frac = val - Math.Floor(val); var dd = Math.Pow(10, d);
            if (d < S(JsRound(frac * dd)).Length) return 0;
            return JsRound(frac * dd);
        }
        private static int Carry(double val, int d) =>
            d < S(JsRound((val - Math.Floor(val)) * Math.Pow(10, d))).Length ? 1 : 0;
        private static string Flr(double val)
        {
            if (val < 2147483647 && val > -2147483648) return S(val >= 0 ? JsNum.ToInt32(val) : JsNum.ToInt32(val - 1));
            return S(Math.Floor(val));
        }

        private static readonly Regex ReParenOpen = R(@"\( *");
        private static readonly Regex ReParenSpaceClose = R(@" \)");
        private static readonly Regex ReParenClose = R(@"\)");
        private static readonly Regex Re00 = R(@"^00+$");
        private static readonly Regex ReHashQ = R(@"^[#?]+$");
        private static readonly Regex ReHash0 = R(@"^#+0+$");
        private static readonly Regex ReLeadHash = R(@"^#+([0.])");
        private static readonly Regex ReZeroDotHash = R(@"^(0*)\.(#*)$");
        private static readonly Regex ReComma0 = R(@"^#{1,3},##0(\.?)$");
        private static readonly Regex ReCommaDec = R(@"^#,##0\.([#0]*0)$");
        private static readonly Regex ReCommaComma = R(@"^#,#*,#0");
        private static readonly Regex ReCommaCommaStrip = R(@"^#,#*,");
        private static readonly Regex ReDashed = R(@"^([0#]+)(\\?-([0#]+))+$");
        private static readonly Regex ReFracA = R(@"^([#0?]+)( ?)\/( ?)([#0?]+)");
        private static readonly Regex ReFracB = R(@"^# ([#0?]+)( ?)\/( ?)([#0?]+)");
        private static readonly Regex ReDigits = R(@"^[#0?]+$");
        private static readonly Regex ReDigitsDotFlt = R(@"^([#0?]+)\.([#0]+)$");
        private static readonly Regex ReDigitsDotInt = R(@"^([#0]+)\.([#0]+)$");
        private static readonly Regex Re00000 = R(@"^00,000\.([#0]*0)$");
        private static readonly Regex ReTrailDot = R(@"\.[0#?]*$");

        private static string StripParens(string fmt)
        {
            var f = ReplaceFirst(fmt, ReParenOpen, "");
            f = ReplaceFirst(f, ReParenSpaceClose, "");
            return ReplaceFirst(f, ReParenClose, "");
        }

        private static string DashedFmt(string fmt, string o)
        {
            var ri = 0;
            var rev = StrRev(fmt.Replace("\\", ""));
            var replaced = Regex.Replace(rev, "[0#]", m => ri < o.Length ? o[ri++].ToString() : m.Value == "0" ? "0" : "");
            return StrRev(replaced);
        }

        private static string FracA(Match r, double aval, string sign)
        {
            var ri = Math.Min(r.Groups[4].Length, 7);
            var ff = Frac(aval, Math.Pow(10, ri) - 1, false);
            var o = sign;
            var oa = WriteNum("n", r.Groups[1].Value, ff[1]);
            if (oa.Length > 0 && oa[^1] == ' ') oa = Substr(oa, 0, oa.Length - 1) + "0";
            o += oa + r.Groups[2].Value + "/" + r.Groups[3].Value;
            oa = RPad_(ff[2], ri);
            if (oa.Length < r.Groups[4].Length) oa = Hashq(Substr(r.Groups[4].Value, r.Groups[4].Length - oa.Length)) + oa;
            o += oa;
            return o;
        }

        private static string FracB(Match r, double aval, string sign)
        {
            var ri = Math.Min(Math.Max(r.Groups[1].Length, r.Groups[4].Length), 7);
            var ff = Frac(aval, Math.Pow(10, ri) - 1, true);
            var whole = ff[0] != 0 ? S(ff[0]) : (ff[1] != 0 ? "" : "0");
            return sign + whole + " " + (ff[1] != 0
                ? Pad_(ff[1], ri) + r.Groups[2].Value + "/" + r.Groups[3].Value + RPad_(ff[2], ri)
                : Fill(" ", 2 * ri + 1 + r.Groups[2].Length + r.Groups[3].Length));
        }

        private static string WriteNumFlt(string type, string fmt, double val)
        {
            if (type.Length > 0 && type[0] == '(' && !CloseParen.IsMatch(fmt))
            {
                var ffmt = StripParens(fmt);
                if (val >= 0) return WriteNumFlt("n", ffmt, val);
                return "(" + WriteNumFlt("n", ffmt, -val) + ")";
            }
            if (JsNum.CharCodeAt(fmt, fmt.Length - 1) == 44) return WriteNumCm(type, fmt, val);
            if (fmt.IndexOf('%') != -1) return WriteNumPct(type, fmt, val);
            if (fmt.IndexOf('E') != -1) return WriteNumExp(fmt, val, false);
            if (JsNum.CharCodeAt(fmt, 0) == 36) return "$" + WriteNumFlt(type, Substr(fmt, JsNum.CharAt(fmt, 1) == " " ? 2 : 1), val);
            string o;
            Match r;
            double aval = Math.Abs(val);
            var sign = val < 0 ? "-" : "";
            if (Re00.IsMatch(fmt)) return sign + Pad0r(aval, fmt.Length);
            if (ReHashQ.IsMatch(fmt))
            {
                o = Pad0r(val, 0); if (o == "0") o = "";
                return o.Length > fmt.Length ? o : Hashq(Substr(fmt, 0, fmt.Length - o.Length)) + o;
            }
            if ((r = Frac1.Match(fmt)).Success) return WriteNumF1(r, aval, sign);
            if (ReHash0.IsMatch(fmt)) return sign + Pad0r(aval, fmt.Length - fmt.IndexOf('0'));
            if ((r = Dec1.Match(fmt)).Success)
            {
                var hq = Hashq(r.Groups[1].Value);
                o = Rnd(val, r.Groups[1].Length);
                o = Regex.Replace(o, @"^([^\.]+)$", m => m.Groups[1].Value + "." + hq, RegexOptions.None);
                o = ReplaceFirstEval(o, R(@"\.$"), _ => "." + hq);
                o = ReplaceFirstEval(o, R(@"\.(\d*)$"), m => "." + m.Groups[1].Value + Fill("0", hq.Length - m.Groups[1].Length));
                return fmt.Contains("0.", StringComparison.Ordinal) ? o : ReplaceFirst(o, R(@"^0\."), ".");
            }
            fmt = ReLeadHash.Replace(fmt, m => m.Groups[1].Value, 1);
            if ((r = ReZeroDotHash.Match(fmt)).Success)
            {
                var r1len = r.Groups[1].Length;
                o = Rnd(aval, r.Groups[2].Length);
                o = ReplaceFirstEval(o, R(@"\.(\d*[1-9])0*$"), m => "." + m.Groups[1].Value);
                o = ReplaceFirstEval(o, R(@"^(-?\d*)$"), m => m.Groups[1].Value + ".");
                o = ReplaceFirst(o, R(@"^0\."), r1len > 0 ? "0." : ".");
                return sign + o;
            }
            if (ReComma0.IsMatch(fmt)) return sign + Commaify(Pad0r(aval, 0));
            if ((r = ReCommaDec.Match(fmt)).Success)
            {
                var l = r.Groups[1].Length;
                return val < 0 ? "-" + WriteNumFlt(type, fmt, -val)
                    : Commaify(S(Math.Floor(val) + Carry(val, l))) + "." + Pad0(Dec(val, l), l);
            }
            if (ReCommaComma.IsMatch(fmt)) return WriteNumFlt(type, ReplaceFirst(fmt, ReCommaCommaStrip, ""), val);
            if (ReDashed.IsMatch(fmt))
            {
                o = StrRev(WriteNumFlt(type, Regex.Replace(fmt, @"[\\-]", ""), val));
                return DashedFmt(fmt, o);
            }
            if (Phone.IsMatch(fmt))
            {
                o = WriteNumFlt(type, "##########", val);
                return "(" + Substr(o, 0, 3) + ") " + Substr(o, 3, 3) + "-" + Substr(o, 6);
            }
            if ((r = ReFracA.Match(fmt)).Success) return FracA(r, aval, sign);
            if ((r = ReFracB.Match(fmt)).Success) return FracB(r, aval, sign);
            if (ReDigits.IsMatch(fmt))
            {
                o = Pad0r(val, 0);
                if (fmt.Length <= o.Length) return o;
                return Hashq(Substr(fmt, 0, fmt.Length - o.Length)) + o;
            }
            if ((r = ReDigitsDotFlt.Match(fmt)).Success)
            {
                o = JsNum.ToFixed(val, Math.Min(r.Groups[2].Length, 10));
                o = ReplaceFirstEval(o, R(@"([^0])0+$"), m => m.Groups[1].Value);
                var ri = o.IndexOf('.');
                var lres = fmt.IndexOf('.') - ri;
                var rres = fmt.Length - o.Length - lres;
                return Hashq(Substr(fmt, 0, lres) + o + Substr(fmt, fmt.Length - rres));
            }
            if ((r = Re00000.Match(fmt)).Success)
            {
                var l = r.Groups[1].Length;
                var ri = Dec(val, l);
                if (val < 0) return "-" + WriteNumFlt(type, fmt, -val);
                var c = Commaify(Flr(val));
                c = ReplaceFirstEval(c, R(@"^\d,\d{3}$"), m => "0" + m.Value);
                c = ReplaceFirstEval(c, R(@"^\d*$"), m => "00," + (m.Value.Length < 3 ? Pad0(0, 3 - m.Value.Length) : "") + m.Value);
                return c + "." + Pad0(ri, l);
            }
            switch (fmt)
            {
                case "###,##0.00": return WriteNumFlt(type, "#,##0.00", val);
                case "###,###":
                case "##,###":
                case "#,###":
                {
                    var x = Commaify(Pad0r(aval, 0));
                    return x != "0" ? sign + x : "";
                }
                case "###,###.00": return ReplaceFirst(WriteNumFlt(type, "###,##0.00", val), R(@"^0\."), ".");
                case "#,###.00": return ReplaceFirst(WriteNumFlt(type, "#,##0.00", val), R(@"^0\."), ".");
            }
            throw new SsfException("unsupported format |" + fmt + "|");
        }

        private static string ReplaceFirstEval(string s, Regex re, MatchEvaluator ev) => re.Replace(s, ev, 1);

        private static string WriteNumInt(string type, string fmt, double val)
        {
            if (type.Length > 0 && type[0] == '(' && !CloseParen.IsMatch(fmt))
            {
                var ffmt = StripParens(fmt);
                if (val >= 0) return WriteNumInt("n", ffmt, val);
                return "(" + WriteNumInt("n", ffmt, -val) + ")";
            }
            if (JsNum.CharCodeAt(fmt, fmt.Length - 1) == 44) return WriteNumCm(type, fmt, val);
            if (fmt.IndexOf('%') != -1) return WriteNumPct(type, fmt, val);
            if (fmt.IndexOf('E') != -1) return WriteNumExp(fmt, val, true);
            if (JsNum.CharCodeAt(fmt, 0) == 36) return "$" + WriteNumInt(type, Substr(fmt, JsNum.CharAt(fmt, 1) == " " ? 2 : 1), val);
            string o;
            Match r;
            double aval = Math.Abs(val);
            var sign = val < 0 ? "-" : "";
            if (Re00.IsMatch(fmt)) return sign + Pad0(aval, fmt.Length);
            if (ReHashQ.IsMatch(fmt))
            {
                o = S(val); if (val == 0) o = "";
                return o.Length > fmt.Length ? o : Hashq(Substr(fmt, 0, fmt.Length - o.Length)) + o;
            }
            if ((r = Frac1.Match(fmt)).Success) return WriteNumF2(r, aval, sign);
            if (ReHash0.IsMatch(fmt)) return sign + Pad0(aval, fmt.Length - fmt.IndexOf('0'));
            if ((r = Dec1.Match(fmt)).Success)
            {
                var hq = Hashq(r.Groups[1].Value);
                o = S(val);
                o = ReplaceFirstEval(o, R(@"^([^\.]+)$"), m => m.Groups[1].Value + "." + hq);
                o = ReplaceFirstEval(o, R(@"\.$"), _ => "." + hq);
                o = ReplaceFirstEval(o, R(@"\.(\d*)$"), m => "." + m.Groups[1].Value + Fill("0", hq.Length - m.Groups[1].Length));
                return fmt.Contains("0.", StringComparison.Ordinal) ? o : ReplaceFirst(o, R(@"^0\."), ".");
            }
            fmt = ReLeadHash.Replace(fmt, m => m.Groups[1].Value, 1);
            if ((r = ReZeroDotHash.Match(fmt)).Success)
            {
                var r1len = r.Groups[1].Length;
                o = S(aval);
                o = ReplaceFirstEval(o, R(@"\.(\d*[1-9])0*$"), m => "." + m.Groups[1].Value);
                o = ReplaceFirstEval(o, R(@"^(-?\d*)$"), m => m.Groups[1].Value + ".");
                o = ReplaceFirst(o, R(@"^0\."), r1len > 0 ? "0." : ".");
                return sign + o;
            }
            if (ReComma0.IsMatch(fmt)) return sign + Commaify(S(aval));
            if ((r = ReCommaDec.Match(fmt)).Success)
                return val < 0 ? "-" + WriteNumInt(type, fmt, -val) : Commaify(S(val)) + "." + Fill("0", r.Groups[1].Length);
            if (ReCommaComma.IsMatch(fmt)) return WriteNumInt(type, ReplaceFirst(fmt, ReCommaCommaStrip, ""), val);
            if (ReDashed.IsMatch(fmt))
            {
                o = StrRev(WriteNumInt(type, Regex.Replace(fmt, @"[\\-]", ""), val));
                return DashedFmt(fmt, o);
            }
            if (Phone.IsMatch(fmt))
            {
                o = WriteNumInt(type, "##########", val);
                return "(" + Substr(o, 0, 3) + ") " + Substr(o, 3, 3) + "-" + Substr(o, 6);
            }
            if ((r = ReFracA.Match(fmt)).Success) return FracA(r, aval, sign);
            if ((r = ReFracB.Match(fmt)).Success) return FracB(r, aval, sign);
            if (ReDigits.IsMatch(fmt))
            {
                o = S(val);
                if (fmt.Length <= o.Length) return o;
                return Hashq(Substr(fmt, 0, fmt.Length - o.Length)) + o;
            }
            if ((r = ReDigitsDotInt.Match(fmt)).Success)
            {
                o = JsNum.ToFixed(val, Math.Min(r.Groups[2].Length, 10));
                o = ReplaceFirstEval(o, R(@"([^0])0+$"), m => m.Groups[1].Value);
                var ri = o.IndexOf('.');
                var lres = fmt.IndexOf('.') - ri;
                var rres = fmt.Length - o.Length - lres;
                return Hashq(Substr(fmt, 0, lres) + o + Substr(fmt, fmt.Length - rres));
            }
            if ((r = Re00000.Match(fmt)).Success)
            {
                if (val < 0) return "-" + WriteNumInt(type, fmt, -val);
                var c = Commaify(S(val));
                c = ReplaceFirstEval(c, R(@"^\d,\d{3}$"), m => "0" + m.Value);
                c = ReplaceFirstEval(c, R(@"^\d*$"), m => "00," + (m.Value.Length < 3 ? Pad0(0, 3 - m.Value.Length) : "") + m.Value);
                return c + "." + Pad0(0, r.Groups[1].Length);
            }
            switch (fmt)
            {
                case "###,###":
                case "##,###":
                case "#,###":
                {
                    var x = Commaify(S(aval));
                    return x != "0" ? sign + x : "";
                }
                default:
                    if (ReTrailDot.IsMatch(fmt))
                    {
                        var li = fmt.LastIndexOf('.');
                        return WriteNumInt(type, fmt.Substring(0, li), val) + Hashq(fmt.Substring(li));
                    }
                    break;
            }
            throw new SsfException("unsupported format |" + fmt + "|");
        }

        private static string WriteNum(string type, string fmt, double val) =>
            JsNum.IsInt32(val) ? WriteNumInt(type, fmt, val) : WriteNumFlt(type, fmt, val);

        // ── General ───────────────────────────────────────────────────

        private static string StripDecimal(string o) =>
            o.IndexOf('.') == -1 ? o : ReplaceFirstEval(o, R(@"(?:\.0*|(\.\d*[1-9])0+)$"), m => m.Groups[1].Value);

        private static string NormalizeExp(string o)
        {
            if (o.IndexOf('E') == -1) return o;
            o = ReplaceFirstEval(o, R(@"(?:\.0*|(\.\d*[1-9])0+)[Ee]"), m => m.Groups[1].Value + "E");
            return ReplaceFirstEval(o, R(@"(E[+-])(\d)$"), m => m.Groups[1].Value + "0" + m.Groups[2].Value);
        }

        private static string SmallExp(double v)
        {
            var w = v < 0 ? 12 : 11;
            var o = StripDecimal(JsNum.ToFixed(v, 12)); if (o.Length <= w) return o;
            o = JsNum.ToPrecision(v, 10); if (o.Length <= w) return o;
            return JsNum.ToExponential(v, 5);
        }

        private static string LargeExp(double v)
        {
            var o = StripDecimal(JsNum.ToFixed(v, 11));
            return (o.Length > (v < 0 ? 12 : 11) || o == "0" || o == "-0") ? JsNum.ToPrecision(v, 6) : o;
        }

        /// <summary>SSF_general_num.</summary>
        public static string GeneralNum(double v)
        {
            var V = Log10Floor(Math.Abs(v));
            string o;
            if (V >= -4 && V <= -1) o = JsNum.ToPrecision(v, (int)(10 + V));
            else if (Math.Abs(V) <= 9) o = SmallExp(v);
            else if (V == 10) o = Substr(JsNum.ToFixed(v, 10), 0, 12);
            else o = LargeExp(v);
            return StripDecimal(NormalizeExp(o.ToUpperInvariant()));
        }

        /// <summary>SSF_general for a cell value (string, double, bool, null).</summary>
        public static string General(object? v) => v switch
        {
            string s => s,
            bool b => b ? "TRUE" : "FALSE",
            double d => JsNum.IsInt32(d) ? S(d) : GeneralNum(d),
            null => "",
            _ => throw new SsfException("unsupported value in General format: " + v)
        };

        // ── format parsing ────────────────────────────────────────────

        private static List<string> SplitFmt(string fmt)
        {
            var o = new List<string>();
            var inStr = false;
            int i = 0, j = 0;
            for (; i < fmt.Length; ++i)
            {
                switch (fmt[i])
                {
                    case '"': inStr = !inStr; break;
                    case '_': case '*': case '\\': ++i; break;
                    case ';':
                        o.Add(Substr(fmt, j, i - j));
                        j = i + 1;
                        break;
                }
            }
            o.Add(Substr(fmt, j));
            if (inStr) throw new SsfException("Format |" + fmt + "| unterminated string ");
            return o;
        }

        private static readonly Regex AbsTime = R(@"\[[HhMmSs\u0E0A\u0E19\u0E17]*\]");

        /// <summary>SSF fmt_is_date.</summary>
        public static bool IsDate(string fmt)
        {
            int i = 0;
            string c;
            while (i < fmt.Length)
            {
                switch (c = fmt[i].ToString())
                {
                    case "G": if (IsGeneral(fmt, i)) i += 6; i++; break;
                    case "\"":
                        for (; JsNum.CharCodeAt(fmt, ++i) != 34 && i < fmt.Length;) { }
                        ++i; break;
                    case "\\": i += 2; break;
                    case "_": i += 2; break;
                    case "@": ++i; break;
                    case "B": case "b":
                        if (JsNum.CharAt(fmt, i + 1) == "1" || JsNum.CharAt(fmt, i + 1) == "2") return true;
                        return true; // falls through to the date letters
                    case "M": case "D": case "Y": case "H": case "S": case "E":
                    case "m": case "d": case "y": case "h": case "s": case "e": case "g": return true;
                    case "A": case "a": case "上":
                        if (Substr(fmt, i, 3).ToUpperInvariant() == "A/P") return true;
                        if (Substr(fmt, i, 5).ToUpperInvariant() == "AM/PM") return true;
                        if (Substr(fmt, i, 5).ToUpperInvariant() == "上午/下午") return true;
                        ++i; break;
                    case "[":
                    {
                        var o = c;
                        while (JsNum.CharAt(fmt, i++) != "]" && i < fmt.Length) o += JsNum.CharAt(fmt, i);
                        if (AbsTime.IsMatch(o)) return true;
                        break;
                    }
                    case ".":
                    case "0": case "#":
                        while (i < fmt.Length && ("0#?.,E+-%".Contains(c = JsNum.CharAt(fmt, ++i), StringComparison.Ordinal)
                               || (c == "\\" && JsNum.CharAt(fmt, i + 1) == "-" && "0#".Contains(JsNum.CharAt(fmt, i + 2), StringComparison.Ordinal)))) { }
                        break;
                    case "?": while (JsNum.CharAt(fmt, ++i) == c) { } break;
                    case "*": ++i; if (JsNum.CharAt(fmt, i) == " " || JsNum.CharAt(fmt, i) == "*") ++i; break;
                    case "(": case ")": ++i; break;
                    case "1": case "2": case "3": case "4": case "5": case "6": case "7": case "8": case "9":
                        while (i < fmt.Length && "0123456789".Contains(JsNum.CharAt(fmt, ++i), StringComparison.Ordinal)) { }
                        break;
                    case " ": ++i; break;
                    default: ++i; break;
                }
            }
            return false;
        }

        private sealed class Tok
        {
            public string t;
            public string v;
            /// <summary>A '@' token holding a number (JS keeps the number, so .indexOf on it throws).</summary>
            public bool NumT;
            public Tok(string t, string v, bool numT = false) { this.t = t; this.v = v; NumT = numT; }
        }

        private static readonly Regex ReSecFrac = R(@"\.0+$");
        private static readonly Regex ReDollar = R(@"\$([^-\[\]]*)");

        /// <summary>eval_fmt — <paramref name="v"/> is a double or a string.</summary>
        private static string EvalFmt(string fmt, object v, int flen)
        {
            var o_ = new List<Tok>();
            string o;
            int i = 0;
            string c;
            string lst = "t";
            DateCode? dt = null;
            var hr = "H";
            double vn = v is double dv ? dv : double.NaN;
            bool isNum = v is double;

            while (i < fmt.Length)
            {
                c = fmt[i].ToString();
                switch (c)
                {
                    case "G":
                        if (!IsGeneral(fmt, i)) throw new SsfException("unrecognized character " + c + " in " + fmt);
                        o_.Add(new Tok("G", "General")); i += 7; break;
                    case "\"":
                    {
                        var sb = new StringBuilder();
                        int cc;
                        for (; (cc = JsNum.CharCodeAt(fmt, ++i)) != 34 && i < fmt.Length;) sb.Append((char)cc);
                        o_.Add(new Tok("t", sb.ToString())); ++i; break;
                    }
                    case "\\":
                    {
                        var w = JsNum.CharAt(fmt, ++i);
                        var t = (w == "(" || w == ")") ? w : "t";
                        o_.Add(new Tok(t, w)); ++i; break;
                    }
                    case "_": o_.Add(new Tok("t", " ")); i += 2; break;
                    case "@": o_.Add(new Tok("T", isNum ? S(vn) : (string)v, isNum)); ++i; break;
                    case "B": case "b":
                    case "M": case "D": case "Y": case "H": case "S": case "E":
                    case "m": case "d": case "y": case "h": case "s": case "e": case "g":
                    {
                        if ((c == "B" || c == "b") && (JsNum.CharAt(fmt, i + 1) == "1" || JsNum.CharAt(fmt, i + 1) == "2"))
                        {
                            if (dt == null) { dt = ParseDateCode(NumForDate(v), JsNum.CharAt(fmt, i + 1) == "2"); if (dt == null) return ""; }
                            o_.Add(new Tok("X", Substr(fmt, i, 2))); lst = c; i += 2; break;
                        }
                        if (c is "B" or "M" or "D" or "Y" or "H" or "S" or "E") c = c.ToLowerInvariant();
                        if (isNum && vn < 0) return "";
                        if (dt == null) { dt = ParseDateCode(NumForDate(v)); if (dt == null) return ""; }
                        o = c;
                        while (++i < fmt.Length && fmt[i].ToString().ToLowerInvariant() == c) o += c;
                        if (c == "m" && lst.ToLowerInvariant() == "h") c = "M";
                        if (c == "h") c = hr;
                        o_.Add(new Tok(c, o)); lst = c; break;
                    }
                    case "A": case "a": case "上":
                    {
                        var q = new Tok(c, c);
                        if (dt == null) dt = ParseDateCode(NumForDate(v));
                        if (Substr(fmt, i, 3).ToUpperInvariant() == "A/P") { if (dt != null) q.v = dt.H >= 12 ? "P" : "A"; q.t = "T"; hr = "h"; i += 3; }
                        else if (Substr(fmt, i, 5).ToUpperInvariant() == "AM/PM") { if (dt != null) q.v = dt.H >= 12 ? "PM" : "AM"; q.t = "T"; i += 5; hr = "h"; }
                        else if (Substr(fmt, i, 5).ToUpperInvariant() == "上午/下午") { if (dt != null) q.v = dt.H >= 12 ? "下午" : "上午"; q.t = "T"; i += 5; hr = "h"; }
                        else { q.t = "t"; ++i; }
                        if (dt == null && q.t == "T") return "";
                        o_.Add(q); lst = c; break;
                    }
                    case "[":
                    {
                        o = c;
                        while (JsNum.CharAt(fmt, i++) != "]" && i < fmt.Length) o += JsNum.CharAt(fmt, i);
                        if (JsNum.Slice(o, -1) != "]") throw new SsfException("unterminated \"[\" block: |" + o + "|");
                        if (AbsTime.IsMatch(o))
                        {
                            if (dt == null) { dt = ParseDateCode(NumForDate(v)); if (dt == null) return ""; }
                            o_.Add(new Tok("Z", o.ToLowerInvariant()));
                            lst = JsNum.CharAt(o, 1);
                        }
                        else if (o.IndexOf('$') > -1)
                        {
                            var mm = ReDollar.Match(o);
                            var g = mm.Success ? mm.Groups[1].Value : "";
                            o = g.Length > 0 ? g : "$";
                            if (!IsDate(fmt)) o_.Add(new Tok("t", o));
                        }
                        break;
                    }
                    case ".":
                        if (dt != null)
                        {
                            o = c;
                            while (++i < fmt.Length && (c = fmt[i].ToString()) == "0") o += c;
                            o_.Add(new Tok("s", o)); break;
                        }
                        goto case "0";
                    case "0": case "#":
                        o = c;
                        while (++i < fmt.Length && "0#?.,E+-%".Contains(c = fmt[i].ToString(), StringComparison.Ordinal)) o += c;
                        o_.Add(new Tok("n", o)); break;
                    case "?":
                        o = c;
                        while (JsNum.CharAt(fmt, ++i) == c) o += c;
                        o_.Add(new Tok(c, o)); lst = c; break;
                    case "*": ++i; if (JsNum.CharAt(fmt, i) == " " || JsNum.CharAt(fmt, i) == "*") ++i; break;
                    case "(": case ")": o_.Add(new Tok(flen == 1 ? "t" : c, c)); ++i; break;
                    case "1": case "2": case "3": case "4": case "5": case "6": case "7": case "8": case "9":
                        o = c;
                        while (i < fmt.Length && "0123456789".Contains(JsNum.CharAt(fmt, ++i), StringComparison.Ordinal)) o += JsNum.CharAt(fmt, i);
                        o_.Add(new Tok("D", o)); break;
                    case " ": o_.Add(new Tok(c, c)); ++i; break;
                    case "$": o_.Add(new Tok("t", "$")); ++i; break;
                    default:
                        if (",$-+/():!^&'~{}<>=€acfijklopqrtuvwxzP".IndexOf(c, StringComparison.Ordinal) == -1)
                            throw new SsfException("unrecognized character " + c + " in " + fmt);
                        o_.Add(new Tok("t", c)); ++i; break;
                }
            }

            // Scan for date/time parts
            int bt = 0, ss0 = 0;
            lst = "t";
            for (i = o_.Count - 1; i >= 0; --i)
            {
                switch (o_[i].t)
                {
                    case "h": case "H": o_[i].t = hr; lst = "h"; if (bt < 1) bt = 1; break;
                    case "s":
                    {
                        var ssm = ReSecFrac.Match(o_[i].v);
                        if (ssm.Success) ss0 = Math.Max(ss0, ssm.Value.Length - 1);
                        if (bt < 3) bt = 3;
                        lst = o_[i].t; break;
                    }
                    case "d": case "y": case "M": case "e": lst = o_[i].t; break;
                    case "m": if (lst == "s") { o_[i].t = "M"; if (bt < 2) bt = 2; } break;
                    case "X": break;
                    case "Z":
                        if (bt < 1 && Regex.IsMatch(o_[i].v, "[Hh]")) bt = 1;
                        if (bt < 2 && Regex.IsMatch(o_[i].v, "[Mm]")) bt = 2;
                        if (bt < 3 && Regex.IsMatch(o_[i].v, "[Ss]")) bt = 3;
                        break;
                }
            }
            switch (bt)
            {
                case 1:
                    if (dt!.u >= 0.5) { dt.u = 0; ++dt.S; }
                    if (dt.S >= 60) { dt.S = 0; ++dt.M; }
                    if (dt.M >= 60) { dt.M = 0; ++dt.H; }
                    break;
                case 2:
                    if (dt!.u >= 0.5) { dt.u = 0; ++dt.S; }
                    if (dt.S >= 60) { dt.S = 0; ++dt.M; }
                    break;
            }

            // replace fields
            var nstr = "";
            int jj;
            for (i = 0; i < o_.Count; ++i)
            {
                switch (o_[i].t)
                {
                    case "t": case "T": case " ": case "D": break;
                    case "X": o_[i].v = ""; o_[i].t = ";"; break;
                    case "d": case "m": case "y": case "h": case "H": case "M": case "s": case "e": case "b": case "Z":
                        o_[i].v = WriteDate(o_[i].t[0], o_[i].v, dt!, ss0);
                        o_[i].t = "t"; break;
                    case "n": case "?":
                        jj = i + 1;
                        while (jj < o_.Count && (
                            (c = o_[jj].t) == "?" || c == "D" ||
                            ((c == " " || c == "t") && jj + 1 < o_.Count && (o_[jj + 1].t == "?" || o_[jj + 1].t == "t" && o_[jj + 1].v == "/")) ||
                            (o_[i].t == "(" && (c == " " || c == "n" || c == ")")) ||
                            (c == "t" && (o_[jj].v == "/" || o_[jj].v == " " && jj + 1 < o_.Count && o_[jj + 1].t == "?"))
                        ))
                        {
                            o_[i].v += o_[jj].v;
                            o_[jj] = new Tok(";", "");
                            ++jj;
                        }
                        nstr += o_[i].v;
                        i = jj - 1; break;
                    case "G": o_[i].t = "t"; o_[i].v = General(v); break;
                }
            }
            string vv;
            double myv;
            string ostr;
            if (nstr.Length > 0)
            {
                if (nstr[0] == '(')
                {
                    myv = (vn < 0 && nstr[0] == '-' ? -vn : vn);
                    ostr = WriteNum("n", nstr, myv);
                }
                else
                {
                    myv = (vn < 0 && flen > 1 ? -vn : vn);
                    ostr = WriteNum("n", nstr, myv);
                    if (myv < 0 && o_.Count > 0 && o_[0].t == "t")
                    {
                        ostr = Substr(ostr, 1);
                        o_[0].v = "-" + o_[0].v;
                    }
                }
                jj = ostr.Length - 1;
                var decpt = o_.Count;
                for (i = 0; i < o_.Count; ++i)
                {
                    if (o_[i].t != "t")
                    {
                        if (o_[i].NumT) throw new SsfException("indexOf on a number");
                        if (o_[i].v.IndexOf('.') > -1) { decpt = i; break; }
                    }
                }
                var lasti = o_.Count;
                if (decpt == o_.Count && ostr.IndexOf('E') == -1)
                {
                    for (i = o_.Count - 1; i >= 0; --i)
                    {
                        if ("n?".IndexOf(o_[i].t, StringComparison.Ordinal) == -1 || o_[i].t.Length == 0) continue;
                        if (jj >= o_[i].v.Length - 1) { jj -= o_[i].v.Length; o_[i].v = Substr(ostr, jj + 1, o_[i].v.Length); }
                        else if (jj < 0) o_[i].v = "";
                        else { o_[i].v = Substr(ostr, 0, jj + 1); jj = -1; }
                        o_[i].t = "t";
                        lasti = i;
                    }
                    if (jj >= 0 && lasti < o_.Count) o_[lasti].v = Substr(ostr, 0, jj + 1) + o_[lasti].v;
                }
                else if (decpt != o_.Count && ostr.IndexOf('E') == -1)
                {
                    jj = ostr.IndexOf('.') - 1;
                    for (i = decpt; i >= 0; --i)
                    {
                        if ("n?".IndexOf(o_[i].t, StringComparison.Ordinal) == -1 || o_[i].t.Length == 0) continue;
                        var j = o_[i].v.IndexOf('.') > -1 && i == decpt ? o_[i].v.IndexOf('.') - 1 : o_[i].v.Length - 1;
                        vv = Substr(o_[i].v, j + 1);
                        for (; j >= 0; --j)
                        {
                            if (jj >= 0 && (o_[i].v[j] == '0' || o_[i].v[j] == '#')) vv = JsNum.CharAt(ostr, jj--) + vv;
                        }
                        o_[i].v = vv;
                        o_[i].t = "t";
                        lasti = i;
                    }
                    if (jj >= 0 && lasti < o_.Count) o_[lasti].v = Substr(ostr, 0, jj + 1) + o_[lasti].v;
                    jj = ostr.IndexOf('.') + 1;
                    for (i = decpt; i < o_.Count; ++i)
                    {
                        if (("n?(".IndexOf(o_[i].t, StringComparison.Ordinal) == -1 || o_[i].t.Length == 0) && i != decpt) continue;
                        var j = o_[i].v.IndexOf('.') > -1 && i == decpt ? o_[i].v.IndexOf('.') + 1 : 0;
                        vv = Substr(o_[i].v, 0, j);
                        for (; j < o_[i].v.Length; ++j)
                        {
                            if (jj < ostr.Length) vv += JsNum.CharAt(ostr, jj++);
                        }
                        o_[i].v = vv;
                        o_[i].t = "t";
                        lasti = i;
                    }
                }
            }
            for (i = 0; i < o_.Count; ++i)
            {
                if (o_[i].t.Length > 0 && "n?".IndexOf(o_[i].t, StringComparison.Ordinal) > -1)
                {
                    myv = (flen > 1 && vn < 0 && i > 0 && o_[i - 1].v == "-" ? -vn : vn);
                    o_[i].v = WriteNum(o_[i].t, o_[i].v, myv);
                    o_[i].t = "t";
                }
            }
            var retval = new StringBuilder();
            foreach (var t in o_) retval.Append(t.v);
            return retval.ToString();
        }

        private static double NumForDate(object v)
        {
            if (v is double d) return d;
            throw new SsfException("date token applied to text");
        }

        private static readonly Regex CfRegex2 = R(@"\[(=|>[=]?|<[>=]?)(-?\d+(?:\.\d*)?)\]");
        private static readonly Regex CondStart = R(@"\[[=<>]");

        private static bool ChkCond(double v, Match rr)
        {
            if (!rr.Success) return false;
            var thresh = JsNum.ParseFloat(rr.Groups[2].Value);
            switch (rr.Groups[1].Value)
            {
                case "=": if (v == thresh) return true; break;
                case ">": if (v > thresh) return true; break;
                case "<": if (v < thresh) return true; break;
                case "<>": if (v != thresh) return true; break;
                case ">=": if (v >= thresh) return true; break;
                case "<=": if (v <= thresh) return true; break;
            }
            return false;
        }

        private static (int L, string F) ChooseFmt(string f, object v)
        {
            var fmt = SplitFmt(f);
            int l = fmt.Count, lat = fmt[l - 1].IndexOf('@');
            if (l < 4 && lat > -1) --l;
            if (fmt.Count > 4) throw new SsfException("cannot find right format for |" + string.Join("|", fmt) + "|");
            if (v is not double dv) return (4, fmt.Count == 4 || lat > -1 ? fmt[^1] : "@");
            switch (fmt.Count)
            {
                case 1: fmt = lat > -1 ? new List<string> { "General", "General", "General", fmt[0] } : new List<string> { fmt[0], fmt[0], fmt[0], "@" }; break;
                case 2: fmt = lat > -1 ? new List<string> { fmt[0], fmt[0], fmt[0], fmt[1] } : new List<string> { fmt[0], fmt[1], fmt[0], "@" }; break;
                case 3: fmt = lat > -1 ? new List<string> { fmt[0], fmt[1], fmt[0], fmt[2] } : new List<string> { fmt[0], fmt[1], fmt[2], "@" }; break;
            }
            var ff = dv > 0 ? fmt[0] : dv < 0 ? fmt[1] : fmt[2];
            if (fmt[0].IndexOf('[') == -1 && fmt[1].IndexOf('[') == -1) return (l, ff);
            if (CondStart.IsMatch(fmt[0]) || CondStart.IsMatch(fmt[1]))
            {
                var m1 = CfRegex2.Match(fmt[0]);
                var m2 = CfRegex2.Match(fmt[1]);
                return ChkCond(dv, m1) ? (l, fmt[0]) : ChkCond(dv, m2) ? (l, fmt[1]) : (l, fmt[m1.Success && m2.Success ? 2 : 1]);
            }
            return (l, ff);
        }

        /// <summary>SSF_format(fmt, v) — v is a double, string or bool. Throws <see cref="SsfException"/> where SSF throws.</summary>
        public static string Format(string fmt, object? v)
        {
            if (IsGeneral(fmt, 0)) return General(v);
            if (v == null) return "";
            var (l, f) = ChooseFmt(fmt, v);
            if (IsGeneral(f)) return General(v);
            if (v is bool b) v = b ? "TRUE" : "FALSE";
            else if (v is string s0 && s0.Length == 0) return "";
            return EvalFmt(f, v, l);
        }
    }
}

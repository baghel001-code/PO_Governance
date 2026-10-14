namespace Application.Services.V1.Ingest.SheetJs
{
    /// <summary>
    /// <c>new Date(string)</c> as V8 (Chrome) parses it — the ES5 ISO form first, then V8's legacy
    /// fallback parser (src/date/dateparser-inl.h) — reduced to what SheetJS's CSV reader needs:
    /// whether the string is a valid date and its local wall-clock fields. The planners' browsers ran
    /// in India Standard Time; local time is taken as UTC+05:30.
    /// </summary>
    public static class JsDate
    {
        /// <summary>Business time zone offset (IST) in minutes.</summary>
        public const int LocalOffsetMinutes = 330;

        /// <summary>
        /// SheetJS <c>datenum(date)</c> in a browser set to Asia/Kolkata: the wall-clock serial plus 10
        /// seconds, because V8 reports the 1899 base date's offset (LMT +5:21:10) as a whole -321 minutes.
        /// </summary>
        public const double DatenumSkewDays = 10.0 / 86400.0;

        /// <summary>A parsed date as local wall-clock fields (after JS MakeDay/MakeTime normalisation).</summary>
        public readonly record struct Local(int Year, int Month, int Day, int Hour, int Minute, int Second, int Millisecond)
        {
            /// <summary>Days since 1899-12-30 of the wall-clock time (fractional).</summary>
            public double WallSerial
            {
                get
                {
                    var days = DaysFromCivil(Year, Month, Day) - DaysFromCivil(1899, 12, 30);
                    return days + (Hour * 3600000.0 + Minute * 60000.0 + Second * 1000.0 + Millisecond) / 86400000.0;
                }
            }
        }

        private const int KNone = int.MaxValue;

        /// <summary>Parses like <c>new Date(s)</c>; null when the result would be an Invalid Date.</summary>
        public static Local? Parse(string s)
        {
            var sc = new Scanner(s);
            var day = new DayComposer();
            var time = new TimeComposer();
            var tz = new TzComposer();
            var next = ParseES5(sc, day, time, tz);
            if (next.Kind == TokKind.Invalid) return null;
            var hasReadNumber = !day.IsEmpty;
            for (var token = next; token.Kind != TokKind.End; token = sc.Next())
            {
                if (token.Kind == TokKind.Number)
                {
                    hasReadNumber = true;
                    var n = token.Value;
                    if (sc.SkipSymbol(':'))
                    {
                        if (sc.SkipSymbol(':'))
                        {
                            if (!time.IsEmpty) return null;
                            time.Add(n); time.Add(0);
                        }
                        else
                        {
                            if (!time.Add(n)) return null;
                            if (sc.Peek().IsSymbol('.')) sc.Next();
                        }
                    }
                    else if (sc.SkipSymbol('.') && time.IsExpecting(n))
                    {
                        time.Add(n);
                        if (sc.Peek().Kind != TokKind.Number) return null;
                        var ms = ReadMilliseconds(sc.Next());
                        if (ms < 0) return null;
                        time.AddFinal(ms);
                    }
                    else if (tz.IsExpecting(n))
                    {
                        tz.SetAbsoluteMinute(n);
                    }
                    else if (time.IsExpecting(n))
                    {
                        time.AddFinal(n);
                        var peek = sc.Peek();
                        if (peek.Kind != TokKind.End && peek.Kind != TokKind.WhiteSpace && !peek.IsKeywordZ && !peek.IsAsciiSign) return null;
                    }
                    else
                    {
                        if (!day.Add(n)) return null;
                        sc.SkipSymbol('-');
                    }
                }
                else if (token.Kind == TokKind.Keyword)
                {
                    if (token.KwType == KwType.AmPm && !time.IsEmpty) time.SetHourOffset(token.Value);
                    else if (token.KwType == KwType.Month) { day.SetNamedMonth(token.Value); sc.SkipSymbol('-'); }
                    else if (token.KwType == KwType.TimeZone && hasReadNumber) tz.Set(token.Value);
                    else
                    {
                        if (hasReadNumber) return null;
                        if (sc.Peek().Kind == TokKind.Number) return null;
                    }
                }
                else if (token.IsAsciiSign && (tz.IsUtc || !time.IsEmpty))
                {
                    tz.SetSign(token.Sign);
                    int n = 0, length = 0;
                    if (sc.Peek().Kind == TokKind.Number)
                    {
                        var t2 = sc.Next();
                        length = t2.Length;
                        n = t2.Value;
                    }
                    hasReadNumber = true;
                    if (sc.Peek().IsSymbol(':')) { tz.SetAbsoluteHour(n); tz.SetAbsoluteMinute(KNone); }
                    else if (length == 2 || length == 1) { tz.SetAbsoluteHour(n); tz.SetAbsoluteMinute(0); }
                    else if (length == 4 || length == 3) { tz.SetAbsoluteHour(n / 100); tz.SetAbsoluteMinute(n % 100); }
                    else return null;
                }
                else if ((token.IsAsciiSign || token.IsSymbol(')')) && hasReadNumber)
                {
                    return null;
                }
            }
            if (!day.Write(out var y, out var mo, out var d)) return null;
            if (!time.Write(out var h, out var mi, out var se, out var ms2)) return null;
            if (!tz.Write(out var offsetSec)) return null;

            // MakeDay / MakeTime with overflow, then to local wall clock.
            var dayNum = MakeDay(y, mo, d);
            var timeMs = h * 3600000.0 + mi * 60000.0 + se * 1000.0 + ms2;
            var wallMs = dayNum * 86400000.0 + timeMs; // as given (local or UTC wall)
            if (offsetSec is double off) wallMs = wallMs - off * 1000.0 + LocalOffsetMinutes * 60000.0;
            if (Math.Abs(wallMs) > 8.64e15) return null;
            var days = Math.Floor(wallMs / 86400000.0);
            var rem = wallMs - days * 86400000.0;
            var (yy, mm, dd) = CivilFromDays((long)days);
            var hh = (int)(rem / 3600000); rem -= hh * 3600000.0;
            var mn = (int)(rem / 60000); rem -= mn * 60000.0;
            var ss = (int)(rem / 1000); rem -= ss * 1000.0;
            return new Local(yy, mm, dd, hh, mn, ss, (int)rem);
        }

        private static double MakeDay(int year, int month0, int date)
        {
            var ym = year + Math.Floor(month0 / 12.0);
            var mn = ((month0 % 12) + 12) % 12;
            var t = DaysFromCivil((long)ym, mn + 1, 1);
            return t + date - 1;
        }

        /// <summary>Days since 1970-01-01 of a proleptic Gregorian date.</summary>
        public static long DaysFromCivil(long y, int m, int d)
        {
            y -= m <= 2 ? 1 : 0;
            var era = (y >= 0 ? y : y - 399) / 400;
            var yoe = y - era * 400;
            var doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
            var doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
            return era * 146097 + doe - 719468;
        }

        public static (int Y, int M, int D) CivilFromDays(long z)
        {
            z += 719468;
            var era = (z >= 0 ? z : z - 146096) / 146097;
            var doe = z - era * 146097;
            var yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
            var y = yoe + era * 400;
            var doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
            var mp = (5 * doy + 2) / 153;
            var d = doy - (153 * mp + 2) / 5 + 1;
            var m = mp + (mp < 10 ? 3 : -9);
            return ((int)(y + (m <= 2 ? 1 : 0)), (int)m, (int)d);
        }

        private static int ReadMilliseconds(Tok number)
        {
            var n = number.Value;
            var length = number.Length;
            if (length < 3)
            {
                if (length == 1) n *= 100;
                else if (length == 2) n *= 10;
            }
            else if (length > 3)
            {
                if (length > 9) length = 9;
                var factor = 1;
                do { factor *= 10; length--; } while (length > 3);
                n /= factor;
            }
            return n;
        }

        // ── ES5 ISO date-time ────────────────────────────────────────────

        private static Tok ParseES5(Scanner sc, DayComposer day, TimeComposer time, TzComposer tz)
        {
            if (sc.Peek().IsAsciiSign)
            {
                var signTok = sc.Next();
                if (!sc.Peek().IsFixedLengthNumber(6)) return signTok;
                var year = sc.Next().Value;
                if (signTok.Sign < 0 && year == 0) return signTok;
                day.Add(signTok.Sign * year);
            }
            else if (sc.Peek().IsFixedLengthNumber(4))
            {
                day.Add(sc.Next().Value);
            }
            else return sc.Next();

            if (sc.SkipSymbol('-'))
            {
                if (!sc.Peek().IsFixedLengthNumber(2) || !DayComposer.IsMonth(sc.Peek().Value)) return sc.Next();
                day.Add(sc.Next().Value);
                if (sc.SkipSymbol('-'))
                {
                    if (!sc.Peek().IsFixedLengthNumber(2) || !DayComposer.IsDay(sc.Peek().Value)) return sc.Next();
                    day.Add(sc.Next().Value);
                }
            }
            if (!sc.Peek().IsKeywordType(KwType.TimeSeparator))
            {
                if (sc.Peek().Kind != TokKind.End) return sc.Next();
            }
            else
            {
                sc.Next();
                if (!sc.Peek().IsFixedLengthNumber(2) || sc.Peek().Value < 0 || sc.Peek().Value > 24) return Tok.Invalid;
                var hourIs24 = sc.Peek().Value == 24;
                time.Add(sc.Next().Value);
                if (!sc.SkipSymbol(':')) return Tok.Invalid;
                if (!sc.Peek().IsFixedLengthNumber(2) || !TimeComposer.IsMinute(sc.Peek().Value) || (hourIs24 && sc.Peek().Value > 0)) return Tok.Invalid;
                time.Add(sc.Next().Value);
                if (sc.SkipSymbol(':'))
                {
                    if (!sc.Peek().IsFixedLengthNumber(2) || !TimeComposer.IsSecond(sc.Peek().Value) || (hourIs24 && sc.Peek().Value > 0)) return Tok.Invalid;
                    time.Add(sc.Next().Value);
                    if (sc.SkipSymbol('.'))
                    {
                        if (sc.Peek().Kind != TokKind.Number || (hourIs24 && sc.Peek().Value > 0)) return Tok.Invalid;
                        time.Add(ReadMilliseconds(sc.Next()));
                    }
                }
                if (sc.Peek().IsKeywordZ)
                {
                    sc.Next();
                    tz.Set(0);
                }
                else if (sc.Peek().IsSymbol('+') || sc.Peek().IsSymbol('-'))
                {
                    tz.SetSign(sc.Next().Symbol == '+' ? 1 : -1);
                    if (sc.Peek().IsFixedLengthNumber(4))
                    {
                        var hourmin = sc.Next().Value;
                        var hour = hourmin / 100;
                        var min = hourmin % 100;
                        if (!TimeComposer.IsHour(hour) || !TimeComposer.IsMinute(min)) return Tok.Invalid;
                        tz.SetAbsoluteHour(hour);
                        tz.SetAbsoluteMinute(min);
                    }
                    else
                    {
                        if (!sc.Peek().IsFixedLengthNumber(2) || !TimeComposer.IsHour(sc.Peek().Value)) return Tok.Invalid;
                        tz.SetAbsoluteHour(sc.Next().Value);
                        if (!sc.SkipSymbol(':')) return Tok.Invalid;
                        if (!sc.Peek().IsFixedLengthNumber(2) || !TimeComposer.IsMinute(sc.Peek().Value)) return Tok.Invalid;
                        tz.SetAbsoluteMinute(sc.Next().Value);
                    }
                }
                if (sc.Peek().Kind != TokKind.End) return Tok.Invalid;
            }
            if (tz.IsEmpty && time.IsEmpty) tz.Set(0);
            day.IsIsoDate = true;
            return Tok.End;
        }

        // ── composers ────────────────────────────────────────────────────

        private sealed class DayComposer
        {
            private readonly int[] _comp = new int[3];
            private int _index;
            private int _namedMonth = KNone;
            public bool IsIsoDate;
            public bool IsEmpty => _index == 0;
            public bool Add(int n) { if (_index < 3) { _comp[_index++] = n; return true; } return false; }
            public void SetNamedMonth(int n) => _namedMonth = n;
            public static bool IsMonth(int x) => x >= 1 && x <= 12;
            public static bool IsDay(int x) => x >= 1 && x <= 31;

            public bool Write(out int year, out int month0, out int day)
            {
                year = 0; month0 = 0; day = 0;
                if (_index < 1) return false;
                while (_index < 3) _comp[_index++] = 1;
                int y = 0, m, d;
                if (_namedMonth == KNone)
                {
                    if (IsIsoDate || (_index == 3 && !IsDay(_comp[0]))) { y = _comp[0]; m = _comp[1]; d = _comp[2]; }
                    else { m = _comp[0]; d = _comp[1]; if (_index == 3) y = _comp[2]; }
                }
                else
                {
                    m = _namedMonth;
                    if (_index == 1) d = _comp[0];
                    else if (!IsDay(_comp[0])) { y = _comp[0]; d = _comp[1]; }
                    else { d = _comp[0]; y = _comp[1]; }
                }
                if (!IsIsoDate)
                {
                    if (y >= 0 && y <= 49) y += 2000;
                    else if (y >= 50 && y <= 99) y += 1900;
                }
                if (!IsMonth(m) || !IsDay(d)) return false;
                year = y; month0 = m - 1; day = d;
                return true;
            }
        }

        private sealed class TimeComposer
        {
            private readonly int[] _comp = new int[4];
            private int _index;
            private int _hourOffset = KNone;
            public bool IsEmpty => _index == 0;
            public bool Add(int n) { if (_index < 4) { _comp[_index++] = n; return true; } return false; }
            public bool AddFinal(int n)
            {
                if (!Add(n)) return false;
                while (_index < 4) _comp[_index++] = 0;
                return true;
            }
            public void SetHourOffset(int n) => _hourOffset = n;
            public bool IsExpecting(int n) =>
                (_index == 1 && IsMinute(n)) || (_index == 2 && IsSecond(n)) || (_index == 3 && IsMillisecond(n));
            public static bool IsHour(int x) => x >= 0 && x <= 23;
            public static bool IsMinute(int x) => x >= 0 && x <= 59;
            public static bool IsSecond(int x) => x >= 0 && x <= 59;
            public static bool IsHour12(int x) => x >= 0 && x <= 12;
            public static bool IsMillisecond(int x) => x >= 0 && x <= 999;

            public bool Write(out int h, out int m, out int s, out int ms)
            {
                while (_index < 4) _comp[_index++] = 0;
                h = _comp[0]; m = _comp[1]; s = _comp[2]; ms = _comp[3];
                if (_hourOffset != KNone)
                {
                    if (!IsHour12(h)) return false;
                    h %= 12;
                    h += _hourOffset;
                }
                if (!IsHour(h) || !IsMinute(m) || !IsSecond(s) || !IsMillisecond(ms))
                {
                    if (h != 24 || m != 0 || s != 0 || ms != 0) return false;
                }
                return true;
            }
        }

        private sealed class TzComposer
        {
            private int _sign = KNone, _hour = KNone, _minute = KNone;
            public void Set(int offsetInHours) { _sign = offsetInHours < 0 ? -1 : 1; _hour = Math.Abs(offsetInHours); _minute = 0; }
            public void SetSign(int sign) => _sign = sign < 0 ? -1 : 1;
            public void SetAbsoluteHour(int h) => _hour = h;
            public void SetAbsoluteMinute(int m) => _minute = m;
            public bool IsExpecting(int n) => _hour != KNone && _minute == KNone && TimeComposer.IsMinute(n);
            public bool IsUtc => _hour == 0 && _minute == 0;
            public bool IsEmpty => _hour == KNone;

            public bool Write(out double? offsetSeconds)
            {
                offsetSeconds = null;
                if (_sign != KNone)
                {
                    if (_hour == KNone) _hour = 0;
                    if (_minute == KNone) _minute = 0;
                    var total = _sign * (_hour * 3600.0 + _minute * 60.0);
                    if (Math.Abs(total) > 2147483647) return false;
                    offsetSeconds = total;
                }
                return true;
            }
        }

        // ── tokenizer ────────────────────────────────────────────────────

        private enum TokKind { Invalid, Unknown, Number, Symbol, WhiteSpace, Keyword, End }
        private enum KwType { Invalid, Month, TimeZone, TimeSeparator, AmPm }

        private readonly struct Tok
        {
            public TokKind Kind { get; init; }
            public int Value { get; init; }
            public int Length { get; init; }
            public char Symbol { get; init; }
            public KwType KwType { get; init; }

            public static Tok Invalid => new() { Kind = TokKind.Invalid };
            public static Tok End => new() { Kind = TokKind.End };
            public bool IsSymbol(char c) => Kind == TokKind.Symbol && Symbol == c;
            public bool IsAsciiSign => Kind == TokKind.Symbol && (Symbol == '+' || Symbol == '-');
            public int Sign => Symbol == '-' ? -1 : 1;
            public bool IsFixedLengthNumber(int len) => Kind == TokKind.Number && Length == len;
            public bool IsKeywordType(KwType t) => Kind == TokKind.Keyword && KwType == t;
            public bool IsKeywordZ => Kind == TokKind.Keyword && KwType == KwType.TimeZone && Length == 1 && Value == 0 && IsZ;
            public bool IsZ { get; init; }
        }

        private static readonly (string Prefix, KwType Type, int Value)[] Keywords =
        {
            ("jan", KwType.Month, 1), ("feb", KwType.Month, 2), ("mar", KwType.Month, 3), ("apr", KwType.Month, 4),
            ("may", KwType.Month, 5), ("jun", KwType.Month, 6), ("jul", KwType.Month, 7), ("aug", KwType.Month, 8),
            ("sep", KwType.Month, 9), ("oct", KwType.Month, 10), ("nov", KwType.Month, 11), ("dec", KwType.Month, 12),
            ("am\0", KwType.AmPm, 0), ("pm\0", KwType.AmPm, 12),
            ("ut\0", KwType.TimeZone, 0), ("utc", KwType.TimeZone, 0), ("z\0\0", KwType.TimeZone, 0), ("gmt", KwType.TimeZone, 0),
            ("cdt", KwType.TimeZone, -5), ("cst", KwType.TimeZone, -6), ("edt", KwType.TimeZone, -4), ("est", KwType.TimeZone, -5),
            ("mdt", KwType.TimeZone, -6), ("mst", KwType.TimeZone, -7), ("pdt", KwType.TimeZone, -7), ("pst", KwType.TimeZone, -8),
            ("t\0\0", KwType.TimeSeparator, 0)
        };

        private sealed class Scanner
        {
            private readonly string _s;
            private int _pos;
            private Tok _next;

            public Scanner(string s) { _s = s; _next = Scan(); }

            public Tok Next() { var t = _next; _next = Scan(); return t; }
            public Tok Peek() => _next;
            public bool SkipSymbol(char c)
            {
                if (_next.IsSymbol(c)) { _next = Scan(); return true; }
                return false;
            }

            private char Ch => _pos < _s.Length ? _s[_pos] : '\0';
            private bool IsEnd => _pos >= _s.Length;

            private Tok Scan()
            {
                var pre = _pos;
                if (IsEnd) return Tok.End;
                var ch = Ch;
                if (ch >= '0' && ch <= '9')
                {
                    int n = 0, i = 0;
                    while (Ch == '0' && !IsEnd) _pos++;
                    while (!IsEnd && Ch >= '0' && Ch <= '9')
                    {
                        if (i < 9) n = n * 10 + (Ch - '0');
                        i++;
                        _pos++;
                    }
                    return new Tok { Kind = TokKind.Number, Value = n, Length = _pos - pre };
                }
                if (ch is ':' or '-' or '+' or '.' or ')')
                {
                    _pos++;
                    return new Tok { Kind = TokKind.Symbol, Symbol = ch };
                }
                if (ch >= 'A' && !JsNum.IsJsWhiteSpace(ch))
                {
                    var buf = new char[3];
                    var len = 0;
                    while (!IsEnd && Ch >= 'A' && !JsNum.IsJsWhiteSpace(Ch))
                    {
                        if (len < 3) buf[len] = (char)(Ch | 0x20);
                        len++;
                        _pos++;
                    }
                    var pre3 = new string(buf);
                    foreach (var kw in Keywords)
                    {
                        if (kw.Prefix == pre3 && (len <= 3 || kw.Type == KwType.Month))
                            return new Tok { Kind = TokKind.Keyword, KwType = kw.Type, Value = kw.Value, Length = len, IsZ = kw.Prefix == "z\0\0" };
                    }
                    return new Tok { Kind = TokKind.Keyword, KwType = KwType.Invalid, Length = len };
                }
                if (JsNum.IsJsWhiteSpace(ch))
                {
                    while (!IsEnd && JsNum.IsJsWhiteSpace(Ch)) _pos++;
                    return new Tok { Kind = TokKind.WhiteSpace, Length = _pos - pre };
                }
                if (ch == '(')
                {
                    // SkipParentheses: balanced (...) groups are skipped.
                    var balance = 0;
                    do
                    {
                        if (Ch == ')') --balance;
                        else if (Ch == '(') ++balance;
                        _pos++;
                    } while (balance > 0 && !IsEnd);
                    return new Tok { Kind = TokKind.Unknown };
                }
                _pos++;
                return new Tok { Kind = TokKind.Unknown };
            }
        }
    }
}

using System.Text.Json;
using DecisionEngine.Common;

namespace DecisionEngine.Model
{
    /// <summary>
    /// One row of an uploaded data file (F1, F2, BSR, Universe Master …), exactly as the tool stores it:
    /// the original Excel headers as keys and the cell values (string, double, bool or null).
    /// Equivalent of a row object in the browser's <c>S.files[key]</c>, with the same
    /// header-tolerant <see cref="Col"/> lookup as index.html <c>col(row, ...names)</c>.
    /// </summary>
    public sealed class SheetRow
    {
        private readonly List<KeyValuePair<string, object?>> _cells;
        private Dictionary<string, string>? _keyMap;
        private Dictionary<string, int>? _index;

        public SheetRow() => _cells = new();
        public SheetRow(IEnumerable<KeyValuePair<string, object?>> cells) => _cells = cells.ToList();

        /// <summary>Header names in their original order.</summary>
        public IEnumerable<string> Keys => _cells.Select(c => c.Key);
        public IReadOnlyList<KeyValuePair<string, object?>> Cells => _cells;
        public int Count => _cells.Count;

        private Dictionary<string, int> Index
        {
            get
            {
                if (_index != null) return _index;
                var d = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var i = 0; i < _cells.Count; i++) d[_cells[i].Key] = i;
                return _index = d;
            }
        }

        public bool ContainsKey(string key) => Index.ContainsKey(key);

        /// <summary>Raw value by exact header (JS <c>row[key]</c>); null when missing.</summary>
        public object? this[string key]
        {
            get => Index.TryGetValue(key, out var i) ? _cells[i].Value : null;
            set
            {
                if (Index.TryGetValue(key, out var i)) _cells[i] = new(key, value);
                else { _cells.Add(new(key, value)); _index![key] = _cells.Count - 1; }
                _keyMap = null;
            }
        }

        /// <summary>Raw value by exact header as JS String(v) ("" for null / missing).</summary>
        public string Str(string key) => Js.ToJsString(this[key]);

        /// <summary>
        /// index.html <c>col(row, ...names)</c>: the first of <paramref name="names"/> (matched after
        /// whitespace / case normalisation) whose cell is not empty, as a trimmed string; "" otherwise.
        /// </summary>
        public string Col(params string[] names)
        {
            var m = KeyMap;
            foreach (var n in names)
            {
                if (!m.TryGetValue(Js.ColNorm(n), out var k)) continue;
                var v = this[k];
                if (v == null) continue;
                if (v is string s && s.Length == 0) continue;
                return Js.ToJsString(v).Trim();
            }
            return "";
        }

        /// <summary>Normalised header → actual header (later duplicates win, as in JS).</summary>
        public Dictionary<string, string> KeyMap
        {
            get
            {
                if (_keyMap != null) return _keyMap;
                var m = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var c in _cells) m[Js.ColNorm(c.Key)] = c.Key;
                return _keyMap = m;
            }
        }

        public SheetRow Clone() => new(_cells);

        // ── JSON (the stored {"rows":[…]} format) ─────────────────────────

        public static SheetRow FromJson(JsonElement obj)
        {
            var cells = new List<KeyValuePair<string, object?>>();
            foreach (var p in obj.EnumerateObject())
                cells.Add(new(p.Name, FromJsonValue(p.Value)));
            return new SheetRow(cells);
        }

        public static object? FromJsonValue(JsonElement v) => v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => v.GetRawText()
        };

        public void WriteJson(Utf8JsonWriter w)
        {
            w.WriteStartObject();
            foreach (var c in _cells)
            {
                w.WritePropertyName(c.Key);
                switch (c.Value)
                {
                    case null: w.WriteNullValue(); break;
                    case string s: w.WriteStringValue(s); break;
                    case double d when double.IsFinite(d): w.WriteNumberValue(d); break;
                    case double: w.WriteNullValue(); break;
                    case int i: w.WriteNumberValue(i); break;
                    case long l: w.WriteNumberValue(l); break;
                    case decimal m: w.WriteNumberValue(m); break;
                    case bool b: w.WriteBooleanValue(b); break;
                    case DateTime dt: w.WriteStringValue(Js.ToJsString(dt)); break;
                    default: w.WriteStringValue(Js.ToJsString(c.Value)); break;
                }
            }
            w.WriteEndObject();
        }

        /// <summary>Reads {"rows":[…]} JSON (already decompressed).</summary>
        public static List<SheetRow> ReadRows(ReadOnlySpan<byte> json)
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { AllowTrailingCommas = true });
            using var doc = JsonDocument.ParseValue(ref reader);
            var list = new List<SheetRow>();
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("rows", out var rows)
                && rows.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in rows.EnumerateArray())
                    if (r.ValueKind == JsonValueKind.Object) list.Add(FromJson(r));
            }
            return list;
        }

        /// <summary>Writes {"rows":[…]} JSON.</summary>
        public static byte[] WriteRows(IEnumerable<SheetRow> rows)
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            {
                w.WriteStartObject();
                w.WritePropertyName("rows");
                w.WriteStartArray();
                foreach (var r in rows) r.WriteJson(w);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            return ms.ToArray();
        }
    }
}

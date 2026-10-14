using System.Collections;

namespace Application.Services.V1.Engines.Common
{
    /// <summary>
    /// A string-keyed map that enumerates its keys in the order a plain JavaScript object does
    /// (<c>Object.keys</c> / <c>Object.values</c> / <c>for…in</c>): first every "array index" key
    /// (a canonical unsigned integer below 2^32−1, e.g. "7", "101", "1114123897") in ascending numeric
    /// order, then every other key in insertion order. The engines use plain objects as maps keyed by
    /// colour codes, Generic Materials and attribute values, and several results depend on that
    /// iteration order (stable sorts with ties, floating-point sums), so the ports use this map
    /// wherever the JS iterates an object.
    /// </summary>
    public sealed class JsObjectMap<TValue> : IEnumerable<KeyValuePair<string, TValue>>
    {
        private readonly Dictionary<string, TValue> _values = new(StringComparer.Ordinal);
        private readonly List<string> _insertion = new();
        private List<string>? _ordered;

        public int Count => _values.Count;

        public bool ContainsKey(string key) => _values.ContainsKey(key);

        public bool TryGetValue(string key, out TValue value) => _values.TryGetValue(key, out value!);

        /// <summary>JS <c>obj[key]</c> — default(TValue) when missing (the JS <c>undefined</c>).</summary>
        public TValue? Get(string key) => _values.TryGetValue(key, out var v) ? v : default;

        public TValue this[string key]
        {
            get => _values[key];
            set
            {
                if (!_values.ContainsKey(key)) { _insertion.Add(key); _ordered = null; }
                _values[key] = value;
            }
        }

        /// <summary>Keys in JS property order.</summary>
        public IReadOnlyList<string> Keys
        {
            get
            {
                if (_ordered != null) return _ordered;
                var idx = _insertion.Where(IsArrayIndex).OrderBy(k => ulong.Parse(k)).ToList();
                idx.AddRange(_insertion.Where(k => !IsArrayIndex(k)));
                return _ordered = idx;
            }
        }

        /// <summary>Values in JS property order (<c>Object.values</c>).</summary>
        public IEnumerable<TValue> Values => Keys.Select(k => _values[k]);

        public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator() =>
            Keys.Select(k => new KeyValuePair<string, TValue>(k, _values[k])).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Deep-enough copy (values copied by reference / value).</summary>
        public JsObjectMap<TValue> Clone()
        {
            var m = new JsObjectMap<TValue>();
            foreach (var k in _insertion) m[k] = _values[k];
            return m;
        }

        /// <summary>A Dictionary whose (insertion) order equals the JS property order — for DTOs.</summary>
        public Dictionary<string, TValue> ToDictionary()
        {
            var d = new Dictionary<string, TValue>(StringComparer.Ordinal);
            foreach (var k in Keys) d[k] = _values[k];
            return d;
        }

        /// <summary>True when <paramref name="k"/> is a JS array index (canonical uint32 &lt; 2^32−1).</summary>
        public static bool IsArrayIndex(string k)
        {
            if (k.Length == 0 || k.Length > 10) return false;
            if (k == "0") return true;
            if (k[0] < '1' || k[0] > '9') return false;
            foreach (var c in k) if (c < '0' || c > '9') return false;
            return ulong.Parse(k) < 4294967295UL;
        }
    }
}

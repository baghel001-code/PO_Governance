using DecisionEngine.Model;

namespace Application.Services.V1.Ingest.SheetJs
{
    /// <summary>
    /// A plain JS object built by assignment (<c>obj[key] = value</c>), reproducing JS property order:
    /// integer-like keys ("0", "17", "2025" — canonical array indices) come first in ascending numeric
    /// order, all other keys keep their first-insertion order; re-assigning a key keeps its position.
    /// That is the order JSON.stringify wrote the rows in, so the stored rows match v1.1.4.
    /// </summary>
    public sealed class JsObject
    {
        private readonly List<string> _order = new();
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public void Set(string key, object? value)
        {
            if (!_values.ContainsKey(key)) _order.Add(key);
            _values[key] = value;
        }

        public bool TryGet(string key, out object? value) => _values.TryGetValue(key, out value);

        public object? this[string key] => _values.TryGetValue(key, out var v) ? v : null;

        public int Count => _order.Count;

        /// <summary>JS <c>Object.keys(obj)</c> order.</summary>
        public IEnumerable<string> Keys
        {
            get
            {
                var idx = new List<(uint N, string K)>();
                var rest = new List<string>();
                foreach (var k in _order)
                {
                    if (IsArrayIndex(k, out var n)) idx.Add((n, k)); else rest.Add(k);
                }
                if (idx.Count == 0) return rest;
                idx.Sort((a, b) => a.N.CompareTo(b.N));
                return idx.Select(x => x.K).Concat(rest);
            }
        }

        public static bool IsArrayIndex(string k, out uint n)
        {
            n = 0;
            if (k.Length == 0 || k.Length > 10) return false;
            if (k.Length > 1 && k[0] == '0') return false;
            ulong acc = 0;
            foreach (var ch in k)
            {
                if (ch < '0' || ch > '9') return false;
                acc = acc * 10 + (ulong)(ch - '0');
            }
            if (acc >= 4294967295UL) return false;
            n = (uint)acc;
            return true;
        }

        public SheetRow ToSheetRow() => new(Keys.Select(k => new KeyValuePair<string, object?>(k, _values[k])));
    }

    /// <summary>
    /// <c>Array.prototype.sort(comparator)</c> as V8 runs it (TimSort; arrays shorter than 64 are a
    /// single binary-insertion-sorted run). Matters only when the comparator is inconsistent (NaN).
    /// </summary>
    public static class V8Sort
    {
        public static void Sort<T>(List<T> a, Func<T, T, double> cmp)
        {
            var n = a.Count;
            if (n < 2) return;
            // V8 SortCompareUserFn: a NaN comparator result counts as 0.
            var user = cmp;
            cmp = (x, y) => { var v = user(x, y); return double.IsNaN(v) ? 0 : v; };
            if (n < 64)
            {
                var run = CountAndMakeRun(a, 0, n, cmp);
                BinaryInsertionSort(a, 0, run, n, cmp);
                return;
            }
            // Longer arrays: stable merge sort using the same "order < 0" test V8 applies.
            var arr = a.ToArray();
            var tmp = new T[n];
            MergeSort(arr, tmp, 0, n, cmp);
            a.Clear();
            a.AddRange(arr);
        }

        private static bool Lt(double order) => order < 0;

        private static int CountAndMakeRun<T>(List<T> a, int low, int high, Func<T, T, double> cmp)
        {
            var runLength = 2;
            var elementLow = a[low];
            var elementLowPlusOne = a[low + 1];
            var order = cmp(elementLowPlusOne, elementLow);
            var isDescending = Lt(order);
            var previous = elementLowPlusOne;
            for (var idx = low + 2; idx < high; ++idx)
            {
                var current = a[idx];
                order = cmp(current, previous);
                if (isDescending) { if (order >= 0) break; }
                else { if (Lt(order)) break; }
                previous = current;
                ++runLength;
            }
            if (isDescending) a.Reverse(low, runLength);
            return runLength;
        }

        private static void BinaryInsertionSort<T>(List<T> a, int low, int startArg, int high, Func<T, T, double> cmp)
        {
            var start = low == startArg ? startArg + 1 : startArg;
            for (; start < high; ++start)
            {
                var left = low;
                var right = start;
                var pivot = a[start];
                while (left < right)
                {
                    var mid = left + ((right - left) >> 1);
                    var order = cmp(pivot, a[mid]);
                    if (Lt(order)) right = mid; else left = mid + 1;
                }
                for (var p = start; p > left; --p) a[p] = a[p - 1];
                a[left] = pivot;
            }
        }

        private static void MergeSort<T>(T[] a, T[] tmp, int lo, int hi, Func<T, T, double> cmp)
        {
            if (hi - lo < 2) return;
            var mid = (lo + hi) / 2;
            MergeSort(a, tmp, lo, mid, cmp);
            MergeSort(a, tmp, mid, hi, cmp);
            int i = lo, j = mid, k = lo;
            while (i < mid && j < hi) tmp[k++] = Lt(cmp(a[j], a[i])) ? a[j++] : a[i++];
            while (i < mid) tmp[k++] = a[i++];
            while (j < hi) tmp[k++] = a[j++];
            Array.Copy(tmp, lo, a, lo, hi - lo);
        }
    }
}

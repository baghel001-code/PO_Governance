using DecisionEngine.Model;

namespace DecisionEngine.Common
{
    /// <summary>
    /// Small helpers for the JS idioms the engines use on rows and parameters, where C# has no
    /// direct equivalent: <c>a||b||c</c> over raw cell values and <c>S.params.X||default</c>.
    /// </summary>
    public static class JsOps
    {
        /// <summary><c>v||d</c> for a number: 0 and NaN fall back to <paramref name="d"/>.</summary>
        public static double Or(double v, double d) => v == 0 || double.IsNaN(v) ? d : v;

        /// <summary>
        /// <c>r[k1]||r[k2]||…||fallback</c> on exact header names: the first truthy raw cell value,
        /// otherwise <paramref name="fallback"/>.
        /// </summary>
        public static object? RawOr(SheetRow r, object? fallback, params string[] keys)
        {
            foreach (var k in keys)
            {
                var v = r[k];
                if (Js.Truthy(v)) return v;
            }
            return fallback;
        }

        /// <summary><c>String(r[k1]||r[k2]||…||'')</c>.</summary>
        public static string StrOr(SheetRow r, params string[] keys) => Js.ToJsString(RawOr(r, "", keys));

        /// <summary><c>String(v).toUpperCase()</c> for a raw value.</summary>
        public static string Upper(object? v) => Js.ToJsString(v).ToUpperInvariant();

        /// <summary>JS loose truthiness for a nullable number.</summary>
        public static bool Truthy(double? v) => v.HasValue && v.Value != 0 && !double.IsNaN(v.Value);
    }
}

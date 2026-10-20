namespace DecisionEngine.Model
{
    /// <summary>Data-file keys (same keys as v1.1.4 — stored in dataset_versions.dkey).</summary>
    public static class DatasetKeys
    {
        public const string F1 = "f1";              // Stock
        public const string F2 = "f2";              // Pending PO
        public const string F3 = "f3";              // Sales trend
        public const string F4 = "f4";              // Forecast / plan
        public const string Um = "um";              // Universe Master
        public const string Bsr = "bsr";            // BSR — all seasons
        public const string ArtMaster = "artmaster";
        public const string ColMaster = "colmaster";
        public const string AttrRel = "attrrel";
        public const string CatHeadMap = "catheadmap"; // SCR products
        public const string Grc = "grc";            // GRC buying history (back-testing)

        public static readonly string[] All = { F1, F2, F3, F4, Um, Bsr, ArtMaster, ColMaster, AttrRel, CatHeadMap, Grc };
        public static readonly string[] Required = { F1, F2, F3, F4, Um, Bsr };

        public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
        {
            [F1] = "F1 — Stock", [F2] = "F2 — Pending PO", [F3] = "F3 — Sales Trend", [F4] = "F4 — Forecast",
            [Um] = "Universe Master", [Bsr] = "BSR — All Seasons", [ArtMaster] = "Article Master",
            [ColMaster] = "Colour Master", [AttrRel] = "Attr Relevance Master", [CatHeadMap] = "Cat Head Mapping",
            [Grc] = "GRC — Buying History"
        };
    }
}

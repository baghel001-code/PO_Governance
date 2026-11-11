namespace Application.Services.V1.Sap
{
    /// <summary>
    /// The MRA verdict page link given by GET /api/sap/v1/verdict-url: built from the decision's verdict link
    /// (&lt;portal&gt;/verdict/&lt;token&gt;) as &lt;portal&gt;/mra-verdict/&lt;token&gt;/&lt;mra no&gt; — same portal, same unguessable token and
    /// expiry, so nothing new is stored.
    /// </summary>
    public static class MraVerdictLinks
    {
        private const string Verdict = "/verdict/", MraVerdict = "/mra-verdict/";

        /// <summary>The token of a decision verdict link, or null when the link is not one.</summary>
        public static string? Token(string? viewUrl)
        {
            if (string.IsNullOrEmpty(viewUrl)) return null;
            var i = viewUrl.LastIndexOf(Verdict, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            var t = viewUrl[(i + Verdict.Length)..].Split('?', '#')[0].TrimEnd('/');
            return t.Length == 0 || t.Contains('/') ? null : t;
        }

        /// <summary>The MRA page link for <paramref name="mraNo"/>, or null when <paramref name="viewUrl"/> is not a verdict link.</summary>
        public static string? PageUrl(string? viewUrl, string mraNo)
        {
            if (Token(viewUrl) is not { } t) return null;
            if (!(viewUrl!.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || viewUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))) return null;
            var i = viewUrl.LastIndexOf(Verdict, StringComparison.OrdinalIgnoreCase);
            return viewUrl[..i] + MraVerdict + Uri.EscapeDataString(t) + "/" + Uri.EscapeDataString(mraNo);
        }
    }
}

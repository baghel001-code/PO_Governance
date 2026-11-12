namespace Application.Services.V1.Sap
{
    /// <summary>
    /// Links of a SAP decision (all on the portal of the decision's link, with its unguessable token and expiry):
    /// <list type="bullet">
    /// <item>view_url of the /decisions answer (since 12 Nov 2026): the MRA page &lt;portal&gt;/mra-verdict/&lt;token&gt;
    /// — the decision and its working; once SAP stores the MRA number with POST /documents also where the MRA is and its
    /// level changes;</item>
    /// <item>GET /verdict-url: &lt;portal&gt;/mra-verdict/&lt;token&gt;/&lt;mra no&gt;;</item>
    /// <item>the PO verdict page &lt;portal&gt;/verdict/&lt;token&gt; (the view_url before; still opens).</item>
    /// </list>
    /// </summary>
    public static class MraVerdictLinks
    {
        private const string Verdict = "/verdict/", MraVerdict = "/mra-verdict/";

        /// <summary>view_url of a new decision: the MRA page of its token.</summary>
        public static string DecisionPage(string portalBase, string token) => portalBase.TrimEnd('/') + MraVerdict + token;

        private static (string Base, string Token)? Split(string? url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (!(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))) return null;
            var path = url.Split('?', '#')[0];
            int i; string rest;
            if ((i = path.LastIndexOf(MraVerdict, StringComparison.OrdinalIgnoreCase)) >= 0) rest = path[(i + MraVerdict.Length)..].Split('/')[0];
            else if ((i = path.LastIndexOf(Verdict, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                rest = path[(i + Verdict.Length)..].TrimEnd('/');
                if (rest.Contains('/')) return null;
            }
            else return null;
            return rest.Length == 0 ? null : (path[..i], rest);
        }

        /// <summary>The token of a decision link (PO verdict page or MRA page), or null when the link is not one.</summary>
        public static string? Token(string? viewUrl) => Split(viewUrl)?.Token;

        /// <summary>The MRA page link for <paramref name="mraNo"/> (GET /verdict-url), or null when <paramref name="viewUrl"/> is not a decision link.</summary>
        public static string? PageUrl(string? viewUrl, string mraNo) =>
            Split(viewUrl) is { } s ? s.Base + MraVerdict + Uri.EscapeDataString(s.Token) + "/" + Uri.EscapeDataString(mraNo) : null;

        /// <summary>The PO verdict page (/verdict/&lt;token&gt;) of a decision link, or null.</summary>
        public static string? PoVerdictUrl(string? viewUrl) =>
            Split(viewUrl) is { } s ? s.Base + Verdict + Uri.EscapeDataString(s.Token) : null;
    }
}

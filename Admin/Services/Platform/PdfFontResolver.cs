using PdfSharp.Fonts;

namespace Admin.Services.Platform
{
    /// <summary>
    /// Fonts for PDFKit (it draws everything in "Arial"): Arial from PortalSettings:PdfFontsFolder or the Windows fonts folder
    /// (arial.ttf / arialbd.ttf); where there is none (Linux) the bundled Liberation Sans (Fonts/, SIL OFL 1.1, same
    /// character widths as Arial). Set once at start-up as PDFsharp's GlobalFontSettings.FontResolver.
    /// </summary>
    public sealed class PdfFontResolver : IFontResolver
    {
        private const string Regular = "pdf-regular", Bold = "pdf-bold";
        private readonly string _regular, _bold;

        public PdfFontResolver(string? fontsFolder, string contentRoot)
        {
            (_regular, _bold) = Pick(fontsFolder, contentRoot);
        }

        /// <summary>The regular and bold font files used (for the start-up log).</summary>
        public string Files => _regular + " / " + _bold;

        private static (string, string) Pick(string? folder, string contentRoot)
        {
            var windir = Environment.GetEnvironmentVariable("WINDIR");
            foreach (var dir in new[] { folder, string.IsNullOrEmpty(windir) ? null : Path.Combine(windir, "Fonts") })
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                string r = Path.Combine(dir, "arial.ttf"), b = Path.Combine(dir, "arialbd.ttf");
                if (File.Exists(r) && File.Exists(b)) return (r, b);
            }
            var bundled = Path.Combine(AppContext.BaseDirectory, "Fonts");
            if (!Directory.Exists(bundled)) bundled = Path.Combine(contentRoot, "Fonts");
            return (Path.Combine(bundled, "LiberationSans-Regular.ttf"), Path.Combine(bundled, "LiberationSans-Bold.ttf"));
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new(isBold ? Bold : Regular);

        public byte[] GetFont(string faceName) => File.ReadAllBytes(faceName == Bold ? _bold : _regular);
    }
}

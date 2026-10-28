using System.Drawing;
using System.Runtime.Versioning;
using Admin.Common;

namespace Admin.Auth.Captcha;

/// <summary>
/// QPS VmmCaptchaGenerator: draws the CAPTCHA with the company VmmCaptcha library (Admin/Dlls/VmmCaptcha.dll,
/// <c>VmmCaptcha.Captcha.Generate(captchaLength, angledegree, fontName, fontSize, fontStyle, noiseLines, noiseDots)</c>),
/// with the settings of PortalSettings:Captcha. Only the drawing comes from VmmCaptcha: the answer is kept by
/// <see cref="CaptchaService"/> in server memory, never in a cookie or the page.
/// VmmCaptcha draws with GDI+ (System.Drawing), which .NET 8 supports on Windows only.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VmmCaptchaGenerator : ICaptchaGenerator
{
    private readonly CaptchaSettings _s;

    public VmmCaptchaGenerator(CaptchaSettings settings) => _s = settings;

    public GeneratedCaptcha Generate()
    {
        // A new instance per call: VmmCaptcha.Captcha keeps a System.Random, which is not thread-safe, and this
        // singleton is called from many users' circuits at once.
        var result = new VmmCaptcha.Captcha().Generate(
            _s.LengthOrDefault, Math.Max(_s.AngleDegrees, 0), string.IsNullOrWhiteSpace(_s.FontName) ? "Arial" : _s.FontName,
            _s.FontSize > 0 ? _s.FontSize : 30, _s.Bold ? FontStyle.Bold : FontStyle.Regular,
            Math.Max(_s.NoiseLines, 0), Math.Max(_s.NoiseDots, 0));
        if (string.IsNullOrEmpty(result?.CaptchaText) || string.IsNullOrEmpty(result.CaptchaBase64Image))
            throw new InvalidOperationException("VmmCaptcha returned no image");
        // plain base64 PNG (no "data:" prefix), as the old QPS login page used it
        return new GeneratedCaptcha(result.CaptchaText, result.CaptchaBase64Image);
    }
}

/// <summary>
/// Uses <paramref name="primary"/> (VmmCaptcha) and, the first time it fails (no GDI+: Linux / container, missing fonts),
/// switches to <paramref name="fallback"/> (the built-in PNG generator) for good and logs it once — sign-in keeps working.
/// </summary>
public sealed class FallbackCaptchaGenerator : ICaptchaGenerator
{
    private readonly ICaptchaGenerator _primary;
    private readonly ICaptchaGenerator _fallback;
    private readonly ILogger _log;
    private volatile bool _useFallback;

    public FallbackCaptchaGenerator(ICaptchaGenerator primary, ICaptchaGenerator fallback, ILogger log)
    {
        _primary = primary;
        _fallback = fallback;
        _log = log;
    }

    /// <summary>True once the primary generator failed and the fallback is used.</summary>
    public bool UsingFallback => _useFallback;

    public GeneratedCaptcha Generate()
    {
        if (!_useFallback)
            try
            {
                return _primary.Generate();
            }
            catch (Exception ex)
            {
                _useFallback = true;
                _log.LogError(ex, "VmmCaptcha could not draw a CAPTCHA ({Message}); using the built-in CAPTCHA from now on", ex.Message);
            }
        return _fallback.Generate();
    }
}

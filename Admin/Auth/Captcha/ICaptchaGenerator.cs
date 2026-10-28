namespace Admin.Auth.Captcha;

/// <summary>A new CAPTCHA: the answer (kept on the server only) and the image to show.</summary>
/// <param name="Answer">The text the user must type.</param>
/// <param name="ImageBase64">PNG bytes as base64, without a "data:" prefix.</param>
public sealed record GeneratedCaptcha(string Answer, string ImageBase64);

/// <summary>
/// Draws CAPTCHA images. <see cref="CaptchaService"/> stores the answer and checks it;
/// the generator only makes the text and the picture. The portal uses <see cref="PngCaptchaGenerator"/> (QPS: VmmCaptchaGenerator over the company VmmCaptcha.dll).
/// </summary>
public interface ICaptchaGenerator
{
    GeneratedCaptcha Generate();
}
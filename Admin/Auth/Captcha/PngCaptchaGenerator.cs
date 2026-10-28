using System.IO.Compression;
using System.Security.Cryptography;

namespace Admin.Auth.Captcha;

/// <summary>
/// Draws the CAPTCHA as a PNG without any image library (QPS draws it with the company VmmCaptcha.dll on System.Drawing,
/// which is Windows-only): 5 characters from a set without look-alikes (no 0/O, 1/I/L), each from a built-in 5×7 font,
/// scaled, rotated and shifted on its own, bent by a wave, over a tinted background with noise lines and dots.
/// The text never reaches the page — only the pixels.
/// </summary>
public sealed class PngCaptchaGenerator : ICaptchaGenerator
{
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 5;
    private const int Width = 220, Height = 80;

    /// <summary>Optional fixed code (development only, for automated UI tests); null = random.</summary>
    private readonly string? _fixed;

    public PngCaptchaGenerator(string? fixedCode = null) => _fixed = string.IsNullOrWhiteSpace(fixedCode) ? null : fixedCode.Trim().ToUpperInvariant();

    public GeneratedCaptcha Generate()
    {
        var text = _fixed ?? new string(Enumerable.Range(0, Length).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
        return new GeneratedCaptcha(text, Convert.ToBase64String(Draw(text)));
    }

    /// <summary>RGB pixels of the picture, encoded as PNG.</summary>
    public static byte[] Draw(string text)
    {
        var px = new byte[Width * Height * 3];
        int R(int max) => RandomNumberGenerator.GetInt32(max);

        // background: light vertical gradient with a random tint
        var (br, bg, bb) = (236 + R(16), 236 + R(16), 236 + R(16));
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var shade = y * 18 / Height;
                Set(px, x, y, (byte)(br - shade), (byte)(bg - shade), (byte)(bb - shade / 2));
            }

        // noise dots
        for (var i = 0; i < 260; i++) Set(px, R(Width), R(Height), (byte)(120 + R(110)), (byte)(120 + R(110)), (byte)(120 + R(110)));

        // characters: each scaled, rotated and shifted on its own; a wave bends the whole word
        var waveAmp = 2.5 + R(30) / 10.0;
        var wavePhase = R(628) / 100.0;
        var step = (Width - 24) / Math.Max(text.Length, 1);
        for (var c = 0; c < text.Length; c++)
        {
            if (!Font.TryGetValue(text[c], out var glyph)) continue;
            var scale = 5.4 + R(11) / 10.0;                      // 5.4 … 6.4 px per font cell
            var angle = (R(37) - 18) * Math.PI / 180;           // −18° … +18°
            var cx = 12 + step * c + step / 2.0 + R(5) - 2;
            var cy = Height / 2.0 + R(13) - 6;
            var (cr, cg, cb) = Palette[R(Palette.Length)];
            var (sin, cos) = Math.SinCos(angle);
            var reach = (int)(scale * 5.5) + 2;
            var x0 = (int)cx - reach; var y0 = (int)cy - reach; var size = 2 * reach + 1;
            var ink = new double[size, size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    // 3×3 supersampling: coverage of the pixel by the glyph
                    var hits = 0;
                    for (var sy = 0; sy < 3; sy++)
                        for (var sx = 0; sx < 3; sx++)
                        {
                            var px0 = x0 + x + (sx + .5) / 3 - cx;
                            var py0 = y0 + y + (sy + .5) / 3 - cy - waveAmp * Math.Sin((x0 + x) / 17.0 + wavePhase);
                            var gx = (px0 * cos + py0 * sin) / scale + 2.5;     // back into font cells (5 × 7)
                            var gy = (-px0 * sin + py0 * cos) / scale + 3.5;
                            var ix = (int)Math.Floor(gx);
                            var iy = (int)Math.Floor(gy);
                            if (ix >= 0 && ix < 5 && iy >= 0 && iy < 7 && glyph[iy][ix] == '#') hits++;
                        }
                    ink[y, x] = hits / 9.0;
                }
            // soften the stair-steps of the rotated cells (3×3 blur), then paint
            for (var y = 1; y < size - 1; y++)
                for (var x = 1; x < size - 1; x++)
                {
                    var sum = 0.0;
                    for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -1; dx <= 1; dx++) sum += ink[y + dy, x + dx] * (dx == 0 && dy == 0 ? 4 : dx == 0 || dy == 0 ? 2 : 1);
                    var a = Math.Min(1, sum / 16 * 1.35);
                    if (a > .03) Blend(px, x0 + x, y0 + y, cr, cg, cb, a);
                }
        }

        // noise lines over the text
        for (var i = 0; i < 4; i++)
        {
            var (lr, lg, lb) = Palette[R(Palette.Length)];
            var x0 = R(Width / 3); var y0 = R(Height);
            var x1 = Width - R(Width / 3); var y1 = R(Height);
            var curve = R(20) - 10;
            for (var t = 0; t <= 400; t++)
            {
                var f = t / 400.0;
                var x = x0 + (x1 - x0) * f;
                var y = y0 + (y1 - y0) * f + curve * Math.Sin(f * Math.PI);
                Blend(px, (int)x, (int)y, lr, lg, lb, .8);
                Blend(px, (int)x, (int)y + 1, lr, lg, lb, .45);
            }
        }
        return EncodePng(px, Width, Height);
    }

    private static readonly (byte, byte, byte)[] Palette =
    {
        (15, 108, 177), (212, 0, 124), (10, 74, 125), (98, 30, 120), (20, 110, 70), (160, 60, 0)
    };

    private static void Set(byte[] px, int x, int y, byte r, byte g, byte b)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        var i = (y * Width + x) * 3;
        px[i] = r; px[i + 1] = g; px[i + 2] = b;
    }

    private static void Blend(byte[] px, int x, int y, byte r, byte g, byte b, double a)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        var i = (y * Width + x) * 3;
        px[i] = (byte)(px[i] * (1 - a) + r * a);
        px[i + 1] = (byte)(px[i + 1] * (1 - a) + g * a);
        px[i + 2] = (byte)(px[i + 2] * (1 - a) + b * a);
    }

    // ── PNG (8-bit RGB, no filter, zlib) ────────────────────────
    public static byte[] EncodePng(byte[] rgb, int width, int height)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            for (var y = 0; y < height; y++)
            {
                z.WriteByte(0);   // filter: none
                z.Write(rgb, y * width * 3, width * 3);
            }
        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)width);
        WriteBE(ihdr, 4, (uint)height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 2;   // colour type: RGB
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, (uint)data.Length);
        s.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc32(typeBytes, data);
        var c = new byte[4];
        WriteBE(c, 0, crc);
        s.Write(c);
    }

    private static void WriteBE(byte[] b, int at, uint v)
    {
        b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    public static uint Crc32(params byte[][] parts)
    {
        var c = 0xFFFFFFFFu;
        foreach (var p in parts)
            foreach (var b in p) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    // ── 5×7 font (rows top to bottom, '#' = ink) ────────────────
    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
        ['C'] = new[] { ".####", "#....", "#....", "#....", "#....", "#....", ".####" },
        ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
        ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
        ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
        ['G'] = new[] { ".####", "#....", "#....", "#.###", "#...#", "#...#", ".###." },
        ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['J'] = new[] { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." },
        ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
        ['M'] = new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" },
        ['N'] = new[] { "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#" },
        ['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
        ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
        ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
        ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
        ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
        ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        ['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
        ['W'] = new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#" },
        ['X'] = new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" },
        ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
        ['Z'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" },
        ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
        ['3'] = new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." },
        ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
        ['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
        ['6'] = new[] { ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###." },
        ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
        ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
        ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###." },
    };
}

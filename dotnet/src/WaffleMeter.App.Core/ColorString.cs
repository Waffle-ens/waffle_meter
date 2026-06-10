using System.Globalization;
using System.Text.RegularExpressions;

namespace WaffleMeter.App.Core;

/// <summary>RGBA color with 0–255 channels.</summary>
public readonly record struct ColorRgba(byte R, byte G, byte B, byte A);

/// <summary>
/// Parse/serialize theme color strings, byte-compatible with the React <c>color-utils.ts</c> so theme
/// values round-trip with the existing <c>settings.properties</c>. Accepts hex (#RGB / #RGBA / #RRGGBB
/// / #RRGGBBAA, any case) and <c>rgb()</c>/<c>rgba()</c>; serializes to uppercase <c>#RRGGBB</c> when
/// alpha is full (rgbaToHex) or <c>rgba(r, g, b, a)</c> with alpha rounded to 3 decimals otherwise
/// (rgbaToCss) — mirroring the picker's <c>formatOut</c>.
/// </summary>
public static partial class ColorString
{
    [GeneratedRegex(@"^rgba?\(\s*([+-]?\d+(?:\.\d+)?)\s*,\s*([+-]?\d+(?:\.\d+)?)\s*,\s*([+-]?\d+(?:\.\d+)?)(?:\s*,\s*([+-]?\d+(?:\.\d+)?))?\s*\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RgbLike();

    public static bool TryParse(string? input, out ColorRgba color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string raw = input.Trim();
        if (raw.StartsWith('#'))
        {
            return TryParseHex(raw[1..], out color);
        }

        Match m = RgbLike().Match(raw);
        if (m.Success)
        {
            byte r = ToByte(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            byte g = ToByte(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
            byte b = ToByte(double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
            double a = m.Groups[4].Success
                ? Math.Clamp(double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture), 0, 1)
                : 1.0;
            color = new ColorRgba(r, g, b, ToByte(a * 255));
            return true;
        }

        return false;
    }

    private static bool TryParseHex(string h, out ColorRgba color)
    {
        color = default;
        if (!IsHex(h))
        {
            return false;
        }

        switch (h.Length)
        {
            case 6:
                color = new ColorRgba(Hex(h, 0, 2), Hex(h, 2, 2), Hex(h, 4, 2), 255);
                return true;
            case 8:
                color = new ColorRgba(Hex(h, 0, 2), Hex(h, 2, 2), Hex(h, 4, 2), Hex(h, 6, 2));
                return true;
            case 3:
                color = new ColorRgba(Dup(h[0]), Dup(h[1]), Dup(h[2]), 255);
                return true;
            case 4:
                color = new ColorRgba(Dup(h[0]), Dup(h[1]), Dup(h[2]), Dup(h[3]));
                return true;
            default:
                return false;
        }
    }

    /// <summary>Serialize like the picker's <c>formatOut</c>, with alpha as a float 0–1 (so the slider's
    /// precision is preserved, matching React): alpha&lt;1 → rgba(); else hex when <paramref name="preferHex"/>,
    /// otherwise rgba(...,1).</summary>
    public static string Serialize(byte r, byte g, byte b, double alpha, bool preferHex) =>
        preferHex && alpha >= 1.0 ? Hex(r, g, b) : Css(r, g, b, alpha);

    /// <summary>rgbaToHex: uppercase <c>#RRGGBB</c> (alpha dropped).</summary>
    public static string Hex(byte r, byte g, byte b) =>
        string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");

    /// <summary>rgbaToCss: <c>rgba(r, g, b, a)</c> with alpha rounded to 3 decimals (no trailing zeros).</summary>
    public static string Css(byte r, byte g, byte b, double alpha)
    {
        // JS roundTo uses Math.round (half away from zero for positive alpha) — match it, not banker's.
        double a = Math.Round(Math.Clamp(alpha, 0, 1), 3, MidpointRounding.AwayFromZero);
        return string.Create(CultureInfo.InvariantCulture, $"rgba({r}, {g}, {b}, {a.ToString("0.###", CultureInfo.InvariantCulture)})");
    }

    /// <summary>rgbaToHex for a parsed color (alpha dropped).</summary>
    public static string ToHex(ColorRgba c) => Hex(c.R, c.G, c.B);

    /// <summary>rgbaToCss for a parsed color (alpha from the byte channel).</summary>
    public static string ToCss(ColorRgba c) => Css(c.R, c.G, c.B, c.A / 255.0);

    private static bool IsHex(string s)
    {
        foreach (char ch in s)
        {
            if (!Uri.IsHexDigit(ch))
            {
                return false;
            }
        }

        return s.Length > 0;
    }

    private static byte Hex(string s, int start, int len) =>
        (byte)int.Parse(s.AsSpan(start, len), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static byte Dup(char c) => (byte)int.Parse(new string(c, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static byte ToByte(double n) => (byte)Math.Clamp((int)Math.Round(n, MidpointRounding.AwayFromZero), 0, 255);
}

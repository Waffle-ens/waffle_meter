using System.Collections.Concurrent;
using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Per-nickname glyph fallback. The chosen meter font is applied app-wide, but a BUNDLED font resolved by
/// pack URI carries no comma-separated fallback chain, so a nickname containing a character that face lacks
/// (most often a single rare Hangul syllable) renders as tofu (□). <see cref="ForName"/> checks whether the
/// chosen font can render every character of a name and, if not, swaps that one name cell to a safe
/// Korean-complete font (Malgun Gothic). Results are cached per (font, text) because the overlay rebuilds
/// rows every tick.
/// </summary>
public static class GlyphFallback
{
    private static readonly FontFamily Fallback = new("Malgun Gothic");
    private static readonly ConcurrentDictionary<(string Font, string Text), FontFamily> Cache = new();

    /// <summary>The font to use for <paramref name="text"/>: the chosen font if it can render every glyph,
    /// else a safe Korean-complete fallback. Never null.</summary>
    public static FontFamily ForName(string chosenFont, string? text)
    {
        FontFamily chosen = FontResolver.Resolve(chosenFont);
        if (string.IsNullOrEmpty(text))
        {
            return chosen;
        }

        return Cache.GetOrAdd((chosenFont, text), _ => CanRenderAll(chosen, text) ? chosen : Fallback);
    }

    private static bool CanRenderAll(FontFamily family, string text)
    {
        GlyphTypeface? glyphs = null;
        foreach (Typeface tf in family.GetTypefaces())
        {
            if (tf.TryGetGlyphTypeface(out GlyphTypeface? g))
            {
                glyphs = g;
                break;
            }
        }

        // Can't introspect (e.g. a system-name family string) — trust WPF's own per-glyph fallback chain.
        if (glyphs == null)
        {
            return true;
        }

        for (int i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            if (cp is ' ' or '\t')
            {
                continue;
            }

            if (!glyphs.CharacterToGlyphMap.ContainsKey(cp))
            {
                return false;
            }
        }

        return true;
    }
}

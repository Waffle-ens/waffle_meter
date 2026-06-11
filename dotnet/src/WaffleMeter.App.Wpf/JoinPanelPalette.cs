using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>Per-job card colors for the join panel (dark tone), ported from the web JOB_COLOR_MAP /
/// getClassColor. <see cref="Card"/> is the slate-950 card fill, <see cref="Border"/> the faint job
/// outline, <see cref="Accent"/> the 4px left bar.</summary>
public readonly record struct JobColors(Brush Card, Brush Border, Brush Accent, Brush BadgeBg);

/// <summary>
/// Resolves a Korean class name to its <see cref="JobColors"/>. Mirrors classColor.ts (dark map only —
/// the .NET app ships dark skins; light tones are a future skin concern). Brushes are frozen + cached.
/// </summary>
public static class JoinPanelPalette
{
    // slate-950 @72% — the shared card fill for every known job.
    private static readonly Brush CardFill = Frozen(Color.FromArgb(0xB8, 0x02, 0x06, 0x17));
    // neutral fallback (unknown job): slate-950/70 + white/12 outline + slate-400 accent.
    private static readonly JobColors Neutral = new(
        Frozen(Color.FromArgb(0xB3, 0x02, 0x06, 0x17)),
        Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
        Frozen(Color.FromArgb(0xC7, 0x94, 0xA3, 0xB8)),
        Frozen(Color.FromArgb(0x22, 0x94, 0xA3, 0xB8)));

    private static readonly Dictionary<string, JobColors> Map = Build();

    /// <summary>Job colors for a Korean class name (null/unknown → neutral).</summary>
    public static JobColors For(string? job)
        => job is not null && Map.TryGetValue(job, out JobColors c) ? c : Neutral;

    private static Dictionary<string, JobColors> Build()
    {
        // (border = job-300, accent = job-400/500) from classColor.ts dark map.
        (string Job, (byte R, byte G, byte B) Border, (byte R, byte G, byte B) Accent)[] defs =
        [
            ("검성", (0x67, 0xE8, 0xF9), (0x22, 0xD3, 0xEE)), // cyan
            ("수호성", (0x93, 0xC5, 0xFD), (0x60, 0xA5, 0xFA)), // blue
            ("살성", (0xBE, 0xF2, 0x64), (0x84, 0xCC, 0x16)), // lime
            ("궁성", (0x6E, 0xE7, 0xB7), (0x34, 0xD3, 0x99)), // emerald
            ("마도성", (0xC4, 0xB5, 0xFD), (0xA7, 0x8B, 0xFA)), // violet
            ("정령성", (0xF0, 0xAB, 0xFC), (0xD9, 0x46, 0xEF)), // fuchsia
            ("치유성", (0xFC, 0xD3, 0x4D), (0xF5, 0x9E, 0x0B)), // amber
            ("호법성", (0xFD, 0xBA, 0x74), (0xF9, 0x73, 0x16)), // orange
        ];

        var map = new Dictionary<string, JobColors>();
        foreach (var d in defs)
        {
            map[d.Job] = new JobColors(
                CardFill,
                Frozen(Color.FromArgb(0x38, d.Border.R, d.Border.G, d.Border.B)),  // ~22% alpha
                Frozen(Color.FromArgb(0xC7, d.Accent.R, d.Accent.G, d.Accent.B)),  // ~78% alpha
                Frozen(Color.FromArgb(0x26, d.Accent.R, d.Accent.G, d.Accent.B))); // badge bg ~15% alpha
        }

        return map;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

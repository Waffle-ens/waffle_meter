namespace WaffleMeter.App.Core;

/// <summary>
/// One buff-overlay preset slot: a named snapshot of the whole combat-assist buff configuration — the
/// per-job 표시/음성 choice (the hidden + voice base-code sets) plus the overlay's display options.
///
/// The master <see cref="MeterSettings.ShowBuffUi"/> toggle is deliberately NOT part of a preset: picking a
/// slot must never turn the overlay itself on or off. Nor is <see cref="MeterSettings.BuffUiObserved"/> —
/// the catalog of buffs ever seen is shared across slots, so a preset can't shrink it.
/// </summary>
public sealed record BuffPreset
{
    public string Name { get; init; } = "";

    public bool Transparent { get; init; } = true;

    public int IconSize { get; init; } = 40;

    public string TextColor { get; init; } = "#FFFFFF";

    public bool TtsOnStart { get; init; }

    public bool TtsOnEnd { get; init; }

    public bool GrayOnCooldown { get; init; }

    public bool ShowOther { get; init; } = true;

    /// <summary>Comma-separated base skill codes hidden from the overlay (a verbatim copy of buffUi.hidden,
    /// kept as the raw CSV so it can be assigned straight back without a lossy set round-trip).</summary>
    public string Hidden { get; init; } = "";

    /// <summary>Comma-separated base skill codes that fire the start/end voice alert (buffUi.voice).</summary>
    public string Voice { get; init; } = "";
}

/// <summary>Every preset slot plus the one currently applied. Persisted as a single Base64(JSON) value —
/// see <see cref="BuffPresetCodec"/>.</summary>
public sealed record BuffPresetSet
{
    public int Active { get; init; }

    public List<BuffPreset> Slots { get; init; } = new();
}

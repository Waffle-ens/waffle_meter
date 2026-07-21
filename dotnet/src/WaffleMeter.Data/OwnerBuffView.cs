namespace WaffleMeter.Data;

/// <summary>
/// One active buff on the local player, as surfaced to the combat-assist overlay + voice alerts.
/// <para><see cref="Code"/> is the BASE skill code (level/rank-independent), so the same buff cast by two
/// different players collapses to one slot and one alert.</para>
/// <para><see cref="Overlay"/> is false for a "음성만" (voice-only) buff — it is returned so the announce path
/// can speak it, but the overlay must not draw it.</para>
/// <para><see cref="EndMs"/> is the absolute expiry (same clock as the caller's <c>nowMs</c>), used to re-arm
/// the end alert when a re-cast extends the buff.</para>
/// <para><see cref="OnCooldown"/> is true when the skill granting this buff is still on cooldown (from the
/// 0x3847 snapshot) — the overlay grays the icon when the user enables that option.</para>
/// <para><see cref="Indefinite"/> is true for an actively-maintained stance with no packet-declared expiry
/// (폭주/권성): its <see cref="RemainingMs"/>/<see cref="EndMs"/> are a synthetic keep-alive, not a real
/// countdown, so the overlay draws no ring/timer and the voice alert never pre-warns its (guessed) end.</para>
/// </summary>
public readonly record struct OwnerBuffView(
    int Code,
    string Name,
    long RemainingMs,
    long DurationMs,
    long EndMs,
    bool ByOther,
    bool Overlay,
    bool OnCooldown,
    bool Indefinite);

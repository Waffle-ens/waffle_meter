namespace WaffleMeter.App.Core;

/// <summary>
/// Carries a remembered 오드 balance forward over the time the meter was not watching.
///
/// <para><b>Why this exists.</b> The game broadcasts the balance only on login/zone-in (0x610B) and when it
/// changes (0x610C), so while the meter runs the number is exact. It is the time BETWEEN sessions that drifts:
/// 자연회복 오드 keeps accruing on the server whether or not anyone is logged in, so a balance recorded last
/// night is already stale by morning — and the badge would show the old number until the game next chose to
/// speak, which can be the better part of a day.</para>
///
/// <para><b>Only the 자연회복 pool moves.</b> 추가 오드 comes from 소모품 and grants; nothing accrues it on a
/// timer, so it is carried across untouched. The two pools also cap separately.</para>
///
/// <para><b>This is an estimate, and it can only ever be too HIGH.</b> Accrual is the one thing that happens
/// while the meter is off; spending is not (you cannot spend what you are not logged in to spend) — unless the
/// player ran the game with the meter closed, which is exactly the case this cannot see. The projection is
/// therefore a floor-of-elapsed-ticks (never rounding up) and is discarded outright by the first live
/// broadcast, which is authoritative.</para>
/// </summary>
public static class AetherRegen
{
    /// <summary>자연회복 오드 granted per tick.</summary>
    public const int NaturalTickAmount = 15;

    /// <summary>How often the server grants <see cref="NaturalTickAmount"/> — three hours, i.e. +120 a day.</summary>
    public const long NaturalTickIntervalMs = 3L * 60 * 60 * 1000;

    /// <summary>Where accrual stops for the 자연회복 pool (the number outside the parentheses).
    /// <para>A GROWTH ceiling, not a correction: a reading already above it is passed through untouched. The cap
    /// is what the game is understood to allow, not something this code measured — so a balance over it is far
    /// more likely to mean our understanding is out of date than that the reading is wrong, and silently editing
    /// an observed number downward would be the worse failure by a distance.</para></summary>
    public const int BaseCap = 840;

    /// <summary>The matching ceiling on the 추가 pool. Nothing accrues that pool on a timer, so this is recorded
    /// for callers that want it rather than used here.</summary>
    public const int BonusCap = 2000;

    /// <summary>Sanity bound on a single pool, matching the parser's. Only a hand-edited settings file can
    /// produce a value beyond this; clamping keeps it from rendering as a plausible-looking balance.</summary>
    private const int SanityMax = 10_000;

    /// <summary>How far a reading may be carried forward. Past this the projection stops rather than saturating:
    /// a month-old record would otherwise always render as a confident <see cref="BaseCap"/>, which looks like a
    /// measurement and is really just "we have no idea". Inside the window the estimate is sound — the only
    /// thing that moves the 자연회복 pool while the meter is off is the server's own timer. Also the guard that
    /// keeps a hand-edited or 1970-epoch timestamp from projecting to the cap.</summary>
    public const long MaxProjectionMs = 7L * 24 * 60 * 60 * 1000;

    /// <summary>The balance as it should stand at <paramref name="nowMs"/>, given a reading of
    /// (<paramref name="baseVal"/>, <paramref name="bonus"/>) taken at <paramref name="savedAtMs"/>.
    /// <para>Returns the reading unchanged when there is nothing to project: no timestamp, a clock that has gone
    /// backwards, less than one full tick elapsed, a pool already at its cap, or a reading older than
    /// <see cref="MaxProjectionMs"/>.</para></summary>
    public static (int Base, int Bonus) Project(int baseVal, int bonus, long savedAtMs, long nowMs)
    {
        int b = Math.Clamp(baseVal, 0, SanityMax);
        int carried = Math.Clamp(bonus, 0, SanityMax);

        long elapsed = nowMs - savedAtMs;
        if (savedAtMs <= 0 || elapsed <= 0 || elapsed > MaxProjectionMs || b >= BaseCap)
        {
            return (b, carried);
        }

        long ticks = elapsed / NaturalTickIntervalMs;
        if (ticks <= 0)
        {
            return (b, carried);
        }

        // long arithmetic on purpose: a hand-edited timestamp from 1970 would overflow an int here.
        long grown = b + (ticks * NaturalTickAmount);
        return ((int)Math.Min(grown, BaseCap), carried);
    }

    /// <summary>How many 자연회복 ticks a reading taken at <paramref name="savedAtMs"/> has missed by
    /// <paramref name="nowMs"/>. Exposed for the tooltip that tells the user the badge is carrying an estimate.</summary>
    public static long TicksSince(long savedAtMs, long nowMs) =>
        savedAtMs <= 0 || nowMs - savedAtMs is <= 0 or > MaxProjectionMs
            ? 0
            : (nowMs - savedAtMs) / NaturalTickIntervalMs;
}

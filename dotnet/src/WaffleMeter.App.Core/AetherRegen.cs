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
/// <para><b>The tick is a server-wide clock grid, not a per-character stopwatch.</b> Every character's 자연회복
/// lands on the same three-hourly boundary — 02:00 / 05:00 / 08:00 / … KST — so what has to be counted is
/// BOUNDARIES CROSSED, not elapsed time divided by three hours. The two models disagree constantly and the
/// grid is the one the game plays by: a reading 29 minutes older than the next one gained a full +15 across a
/// 20:00 boundary, which elapsed-time division cannot produce at all. Measured over 199 capture logs
/// (2026-05-28 → 08-07, 331 오드 records, 7 characters): 13 of 13 in-session ticks land on the grid, 181
/// no-change stretches contain no unexplained boundary, and every competing period — 1 h, 6 h, 12 h, 24 h, and
/// 3 h at other phases — is contradicted. The phase is expressed in epoch arithmetic rather than a local hour
/// because KST is +9, an exact multiple of three: the two are the same grid, and epoch has no DST or timezone
/// to get wrong.</para>
///
/// <para><b>Only the 자연회복 pool moves.</b> 추가 오드 comes from 소모품 and grants; nothing accrues it on a
/// timer (35 in-session increases examined, not one explained by a boundary), so it is carried across
/// untouched. The pools cap separately, at 840 and 2000 — both confirmed on the wire, the first by a character
/// that sat out 12.7 days and came back at exactly 840 rather than the 1,545 the boundaries would have paid.</para>
///
/// <para><b>This is an upper bound, and that is the honest way to read it.</b> Accrual is the one thing that
/// happens while the meter is off; spending is not — unless the player ran the game with the meter closed,
/// which is exactly what this cannot see and is the dominant error in practice (52 of 78 offline stretches in
/// the corpus came back BELOW the prediction, never above). Phase error is bounded at a single tick and is rare
/// besides: the tick itself fires within an eight-second window, so only a reading landing inside it can be
/// mis-counted. The first live broadcast discards all of this and is authoritative.</para>
/// </summary>
public static class AetherRegen
{
    /// <summary>자연회복 오드 granted per tick.</summary>
    public const int NaturalTickAmount = 15;

    /// <summary>How often the server pays <see cref="NaturalTickAmount"/> — three hours, i.e. +120 a day.</summary>
    public const long TickIntervalMs = 3L * 60 * 60 * 1000;

    /// <summary>Where the grid sits: a tick falls at every epoch-ms <c>t</c> with
    /// <c>t mod TickIntervalMs == TickPhaseMs</c>, which is 02:00 / 05:00 / 08:00 / … KST.
    /// <para>Ten seconds past the hour, not on it. The tick was pinned to a HH:00:06-HH:00:14 window by
    /// intersecting 20 independent observation windows that each provably contain exactly one boundary; the
    /// midpoint is the anchor that minimises how often a reading taken right at the turn is counted on the wrong
    /// side. (Arrival is later still — broadcasts were seen 15-57 s behind the tick — but arrival is not the
    /// event.)</para></summary>
    public const long TickPhaseMs = (2L * 60 * 60 * 1000) + 10_000;

    /// <summary>Where accrual stops for the 자연회복 pool (the number outside the parentheses).
    /// <para>A GROWTH ceiling, not a correction: a reading already above it is passed through untouched. The cap
    /// matches every one of 331 observed records, but it is still the game's rule rather than our measurement —
    /// so a balance over it more likely means the rule changed than that the reading is wrong, and silently
    /// editing an observed number downward would be the worse failure by a distance.</para></summary>
    public const int BaseCap = 840;

    /// <summary>The matching ceiling on the 추가 pool (observed maximum, hit exactly, 8 times). Nothing accrues
    /// that pool on a timer, so this is recorded for callers that want it rather than used here.</summary>
    public const int BonusCap = 2000;

    /// <summary>How far a reading may be carried forward. Past this the projection stops rather than saturating:
    /// a month-old record would otherwise always render as a confident <see cref="BaseCap"/>, which looks like a
    /// measurement and is really just "we have no idea". Also the guard that keeps a hand-edited or 1970-epoch
    /// timestamp from projecting to the cap.</summary>
    public const long MaxProjectionMs = 7L * 24 * 60 * 60 * 1000;

    /// <summary>Sanity bound on a single pool, matching the parser's. Only a hand-edited settings file can
    /// produce a value beyond this; clamping keeps it from rendering as a plausible-looking balance.</summary>
    private const int SanityMax = 10_000;

    /// <summary>The balance as it should stand at <paramref name="nowMs"/>, given a reading of
    /// (<paramref name="baseVal"/>, <paramref name="bonus"/>) taken at <paramref name="savedAtMs"/>.
    /// <para>Returns the reading unchanged when there is nothing to project: no timestamp, a clock that has gone
    /// backwards, no tick boundary crossed, a pool already at its cap, or a reading older than
    /// <see cref="MaxProjectionMs"/>.</para></summary>
    public static (int Base, int Bonus) Project(int baseVal, int bonus, long savedAtMs, long nowMs)
    {
        int b = Math.Clamp(baseVal, 0, SanityMax);
        int carried = Math.Clamp(bonus, 0, SanityMax);

        if (b >= BaseCap)
        {
            return (b, carried);
        }

        long ticks = TicksBetween(savedAtMs, nowMs);
        if (ticks <= 0)
        {
            return (b, carried);
        }

        // long arithmetic on purpose: a week of ticks is small, but the inputs are not all ours to trust.
        long grown = b + (ticks * NaturalTickAmount);
        return ((int)Math.Min(grown, BaseCap), carried);
    }

    /// <summary>How many 자연회복 ticks fall in <c>(fromMs, toMs]</c> — the boundaries actually crossed, which
    /// is not the same as the elapsed time divided by the interval. 0 when the span is empty, inverted, or older
    /// than <see cref="MaxProjectionMs"/>.</summary>
    public static long TicksBetween(long fromMs, long toMs)
    {
        long elapsed = toMs - fromMs;
        if (fromMs <= 0 || elapsed <= 0 || elapsed > MaxProjectionMs)
        {
            return 0;
        }

        return GridIndex(toMs) - GridIndex(fromMs);
    }

    /// <summary>Index of the last tick boundary at or before <paramref name="ms"/>. The difference of two of
    /// these is the number of boundaries strictly after the first and up to the second.</summary>
    private static long GridIndex(long ms)
    {
        long offset = ms - TickPhaseMs;
        long q = offset / TickIntervalMs;

        // C# truncates toward zero; the grid needs a floor, or a pre-1970 timestamp would count backwards.
        return offset < 0 && q * TickIntervalMs != offset ? q - 1 : q;
    }
}

namespace WaffleMeter.Data;

/// <summary>One participant's buff-normalized damage numbers.</summary>
/// <param name="Ndps">
/// Normalized DPS — what this player would have done without the buffs OTHER people put on them, and without
/// the damage another class's effect dealt through them. Their own buffs stay in: they are their own play.
/// </param>
/// <param name="Rdps">
/// Raid DPS — <see cref="Ndps"/> plus everything this player enabled in everyone else: the share of each
/// teammate's damage their buffs are responsible for, plus the damage their effects dealt on teammates'
/// meters (흡혈의 검's 착취, 대지의 축복's 추가 피해).
/// </param>
/// <param name="TakenBuffDps">The part of this player's raw DPS that other people's buffs are responsible
/// for — <c>dps - ndps</c>, never negative. Shown so a DPS class can see how much of their number was lent.</param>
/// <param name="GivenBuffDps">What this player lent out, i.e. <c>rdps - ndps</c>. This is the support number.</param>
/// <param name="GrantedDamage">Damage this player's effects dealt ON OTHER PLAYERS' meters over the fight,
/// in raw damage (not per second) — the measured half of <see cref="GivenBuffDps"/>.</param>
public readonly record struct DpsMetricResult(
    double Ndps,
    double Rdps,
    double TakenBuffDps,
    double GivenBuffDps,
    long GrantedDamage);

/// <summary>One buff/debuff as the metric model sees it.</summary>
/// <param name="DisplayBase">The buff's display base code (<see cref="DataManager.BuffDisplayBase"/>), which
/// is what both the synergy catalog and the exclusive-pair table key on.</param>
/// <param name="BossScope">True when this row landed on the boss (a debuff) rather than on a player.</param>
/// <param name="Spans">This buff's merged applied intervals (absolute capture-clock ms), when known. Needed to
/// price an exclusive PAIR honestly: the rate alone cannot say whether two buffs actually overlapped. Empty is
/// allowed and falls back to a conservative rate subtraction.</param>
public readonly record struct MetricBuffInput(
    int Code,
    int DisplayBase,
    int ActorId,
    double OperatingRate,
    int Level,
    bool BossScope,
    IReadOnlyList<(long Start, long End)>? Spans = null);

/// <summary>One participant's inputs.</summary>
/// <param name="GrantedDamageBySource">Damage dealt on THIS player's meter by another class's effect, keyed
/// by the granting synergy base (<see cref="PartySynergyCatalog.GrantedDamageSource"/>). Already filtered to
/// grants from someone else — a 검성's own 흡혈의 검 hits are not in here.</param>
/// <param name="JobPrefix">11(검성)..19(권성), 0 = 모름. 버프 행이 없는 넘어온 피해를 귀속할 때 쓴다.</param>
public sealed record MetricParticipantInput(
    int Uid,
    double Dps,
    double Damage,
    IReadOnlyList<MetricBuffInput> Buffs,
    IReadOnlyDictionary<int, long> GrantedDamageBySource,
    int JobPrefix = 0);

/// <summary>
/// nDPS / rDPS, computed locally with the same shape the stats site uses server-side
/// (<c>src/shared/dps-metrics.ts</c>) so the meter and the site cannot drift into two different definitions:
/// each incoming buff becomes a multiplicative gain from its uptime and its effect values, gains compose as
/// <c>Π(1+gain)−1</c> (capped), <c>ndps = dps / (1 + totalGain)</c>, and each buffer is then credited with
/// <c>recipientNdps × gain</c>.
///
/// <para><b>Where this deliberately improves on the site.</b> The site prices every buff from a fixed
/// snapshot table that has no room for the caster's skill level, and its own source says so — "the payload
/// has no skill level with which to make this rDPS approximation exact". The meter reads that level off the
/// wire, so <see cref="PartySynergyCatalog"/> prices the eight party-synergy buffs from it and the snapshot
/// only fills in the rest. Two further corrections come with it:</para>
/// <list type="number">
/// <item><b>Exclusive pairs.</b> The game applies only one of 노련한 반격/격앙, only one of 보호의 빛/불패의
/// 진언, and suppresses 대지의 축복 under 질풍의 권능 — but the server still broadcasts both, and the loser can
/// linger for seconds. Counting both as gains would credit a support for a buff that was doing nothing. The
/// pair table is shared with the overlay (<see cref="DataManager.ExclusivePairs"/>) so the screen and the
/// math never disagree about which one was live.</item>
/// <item><b>Measured grants.</b> 흡혈의 검's 착취 and 대지의 축복's 추가 피해 land as real damage packets on the
/// recipient's meter under the granting class's skill code. They are moved, not estimated: subtracted from
/// the recipient before normalizing, added to the granter's rDPS whole.</item>
/// </list>
/// </summary>
public static class DpsMetrics
{
    /// <summary>Per-effect cap, matching the site: one effect can at most double the damage it applies to.</summary>
    private const double MaxSingleEffectGain = 1.0;

    /// <summary>Total external gain cap, matching the site. Guards against a corrupt uptime turning into an
    /// absurd divisor and reporting an nDPS of nearly zero.</summary>
    private const double MaxTotalExternalGain = 4.0;

    public static Dictionary<int, DpsMetricResult> Compute(
        IReadOnlyList<MetricParticipantInput> participants,
        IReadOnlyList<MetricBuffInput> bossDebuffs,
        BuffValueCatalog catalog,
        double durationSeconds)
    {
        var results = new Dictionary<int, DpsMetricResult>();
        if (participants.Count == 0)
        {
            return results;
        }

        var uids = participants.Select(p => p.Uid).ToHashSet();

        // uid -> the gains flowing INTO them, tagged with who supplied each.
        var incoming = participants.ToDictionary(p => p.Uid, _ => new List<(int Actor, double Gain)>());

        foreach (MetricParticipantInput participant in participants)
        {
            foreach (MetricBuffInput buff in SurvivingBuffs(participant.Buffs))
            {
                // Self buffs are the player's own play, not something lent to them.
                if (buff.ActorId == participant.Uid) continue;

                // A buff from someone who dealt no damage has no participant row to credit; its gain is still
                // real for the recipient, so it is kept with an unknown actor (-1) rather than dropped.
                double gain = Gain(buff, catalog);
                if (gain <= 0.0) continue;

                incoming[participant.Uid].Add((uids.Contains(buff.ActorId) ? buff.ActorId : -1, gain));
            }
        }

        // A boss debuff helps everyone hitting the boss except the person who applied it (for them it is a
        // self buff). Exclusive-pair suppression does not apply here — the pairs are all player buffs.
        foreach (MetricBuffInput debuff in bossDebuffs)
        {
            double gain = Gain(debuff, catalog);
            if (gain <= 0.0 || !uids.Contains(debuff.ActorId)) continue;

            foreach (MetricParticipantInput participant in participants)
            {
                if (participant.Uid != debuff.ActorId)
                {
                    incoming[participant.Uid].Add((debuff.ActorId, gain));
                }
            }
        }

        // Resolve every grant to the teammate who supplied it, FIRST. Damage whose granter cannot be named —
        // a self-cast (the granting class's own hits carry the same skill code) or a buff whose caster is not
        // a participant — must stay with the player who dealt it. Subtracting it anyway would delete damage
        // from the raid: it would leave the recipient's nDPS and land nowhere.
        var movable = new List<(int Recipient, int Granter, long Damage)>();
        foreach (MetricParticipantInput participant in participants)
        {
            foreach ((int source, long damage) in participant.GrantedDamageBySource)
            {
                if (damage <= 0) continue;

                foreach ((int granter, long share) in
                         SplitGrant(participant, source, damage, uids, participants))
                {
                    movable.Add((participant.Uid, granter, share));
                }
            }
        }

        var movedOut = participants.ToDictionary(p => p.Uid, _ => 0L);
        var granted = participants.ToDictionary(p => p.Uid, _ => 0L);
        foreach ((int recipient, int granter, long damage) in movable)
        {
            movedOut[recipient] += damage;
            granted[granter] += damage;
        }

        // Pass 1 — everyone's own normalized rate, with the movable grants taken out first.
        var ndps = new Dictionary<int, double>();
        foreach (MetricParticipantInput participant in participants)
        {
            double grantedDps = PerSecond(movedOut[participant.Uid], durationSeconds);

            // The grant is another class's damage that merely passed through this player's meter, so it is
            // removed BEFORE normalizing — normalizing it would price it against this player's buffs, and it
            // is not this player's damage at all.
            double ownDps = Math.Max(0.0, participant.Dps - grantedDps);

            double totalGain = Math.Min(
                incoming[participant.Uid].Aggregate(1.0, (m, g) => m * (1.0 + g.Gain)) - 1.0,
                MaxTotalExternalGain);

            ndps[participant.Uid] = ownDps / (1.0 + totalGain);
        }

        // Pass 2 — credit each buffer with the share of the recipient's normalized rate their buff explains,
        // and each granter with the damage their effect actually dealt on someone else's meter.
        var given = participants.ToDictionary(p => p.Uid, _ => 0.0);
        foreach (MetricParticipantInput participant in participants)
        {
            foreach ((int actor, double gain) in incoming[participant.Uid])
            {
                if (actor >= 0 && given.ContainsKey(actor))
                {
                    given[actor] += ndps[participant.Uid] * gain;
                }
            }
        }

        foreach ((int _, int granter, long damage) in movable)
        {
            given[granter] += PerSecond(damage, durationSeconds);
        }

        foreach (MetricParticipantInput participant in participants)
        {
            double n = ndps[participant.Uid];
            double g = given[participant.Uid];
            results[participant.Uid] = new DpsMetricResult(
                Ndps: n,
                Rdps: n + g,
                TakenBuffDps: Math.Max(0.0, participant.Dps - n),
                GivenBuffDps: g,
                GrantedDamage: granted.GetValueOrDefault(participant.Uid));
        }

        return results;
    }

    private static double PerSecond(long damage, double durationSeconds) =>
        durationSeconds > 0 ? damage / durationSeconds : 0.0;

    /// <summary>
    /// Split one grant across everyone who had that synergy buff on this player, in proportion to how long each
    /// of them had it up.
    ///
    /// <para>The damage code cannot tell whose it is — a 검성's own 흡혈의 검 hits carry the same code as the
    /// 착취 they share, and two 검성 in one raid produce one indistinguishable pile on every teammate's meter.
    /// Uptime is the only evidence available about who supplied what, so it is the split.</para>
    ///
    /// <para>The player's OWN share stays with them and is simply not returned: their own hits are their own
    /// damage. A grant with no identifiable outside caster therefore moves nothing, which is the safe direction —
    /// subtracting it anyway would delete damage from the raid, since it would leave the recipient's nDPS and
    /// land nowhere.</para>
    /// </summary>
    private static List<(int Granter, long Damage)> SplitGrant(
        MetricParticipantInput participant,
        int source,
        long damage,
        HashSet<int> uids,
        IReadOnlyList<MetricParticipantInput> others)
    {
        var casters = new List<(int Uid, double Weight)>();
        double total = 0.0;
        foreach (MetricBuffInput buff in participant.Buffs)
        {
            if (buff.DisplayBase != source) continue;

            // An uptime of 0 still means the buff was there; give it a floor so a caster is never weighted out
            // of existence by a rounding artefact.
            double weight = Math.Max(buff.OperatingRate, 0.01);
            casters.Add((buff.ActorId, weight));
            total += weight;
        }

        var result = new List<(int, long)>();

        // 버프 행이 없어도 피해는 실재한다 — 실측에서 파티원 전원이 대지의 축복 추가 피해를 맞는데 버프 행은
        // 시전자에게만 있었다(질풍의 권능이 적용을 막은 파티). 그때는 직업으로 되짚는다: 이 스킬 코드는
        // 직업 전용이라, 그 직업이 아닌 사람 미터에 찍혔다면 그 사람 것일 수 없다.
        //
        // ⚠️ 받은 사람의 직업이 곧 주는 직업이면 되짚지 않는다(치유성이 맞은 대지의 축복은 자기 것일 수도,
        // 다른 치유성 것일 수도 있다). 가릴 근거가 없을 때는 원래 사람에게 남긴다 — 잘못 옮기면 공대 총합에서
        // 그만큼이 증발하거나 엉뚱한 사람에게 붙는다.
        if (casters.Count == 0)
        {
            int grantingJob = PartySynergyCatalog.GrantingJobPrefix(source);
            if (grantingJob == 0 || participant.JobPrefix == grantingJob)
            {
                return result;
            }

            foreach (MetricParticipantInput other in others)
            {
                if (other.Uid == participant.Uid || other.JobPrefix != grantingJob) continue;
                casters.Add((other.Uid, 1.0));
                total += 1.0;
            }
        }

        if (casters.Count == 0 || total <= 0.0)
        {
            return result;
        }

        long assigned = 0;
        for (int i = 0; i < casters.Count; i++)
        {
            (int uid, double weight) = casters[i];

            // The last outside caster absorbs the rounding remainder so the split never loses or invents damage.
            long share = i == casters.Count - 1
                ? damage - assigned
                : (long)Math.Round(damage * (weight / total), MidpointRounding.AwayFromZero);
            assigned += share;

            if (uid == participant.Uid || !uids.Contains(uid) || share <= 0) continue;
            result.Add((uid, share));
        }

        return result;
    }

    /// <summary>
    /// Resolve every exclusive pair present on one player by REDUCING the loser to the time it was actually
    /// alone, rather than deleting it.
    ///
    /// <para>The game applies only one of 노련한 반격/격앙, only one of 보호의 빛/불패의 진언, and blocks a new
    /// 대지의 축복 while 질풍의 권능 is up. The live overlay resolves this by dropping the loser outright, and
    /// there that is right: it is looking at what is active AT THIS INSTANT, so both being present really does
    /// mean one is suppressed.</para>
    ///
    /// <para><b>Here the inputs are whole-battle aggregates, and the same rule would be wrong.</b>
    /// <c>OperatingRate</c> is the union of a buff's applied intervals across the entire fight, so two rows both
    /// existing only means each was up at some point. Deleting the loser would erase a buff that covered 95% of
    /// a 300-second fight because its rival flickered for five seconds. So the loser keeps the part of its
    /// uptime the winner did not cover, computed from the actual intervals; when spans are unavailable it falls
    /// back to subtracting the rates, which is exact when one window contains the other and conservative
    /// otherwise.</para>
    /// </summary>
    private static List<MetricBuffInput> SurvivingBuffs(IReadOnlyList<MetricBuffInput> buffs)
    {
        var kept = buffs.ToList();

        foreach (DataManager.ExclusiveBuffPair pair in DataManager.ExclusivePairs)
        {
            int ai = kept.FindIndex(b => b.DisplayBase == pair.A);
            int bi = kept.FindIndex(b => b.DisplayBase == pair.B);
            if (ai < 0 || bi < 0) continue;

            MetricBuffInput a = kept[ai], b = kept[bi];
            int loserBase = LoserOf(pair, a, b);
            int loserIndex = loserBase == pair.A ? ai : bi;
            MetricBuffInput loser = kept[loserIndex];
            MetricBuffInput winner = loserBase == pair.A ? b : a;

            double remaining = ExclusiveRemainder(loser, winner);
            if (remaining <= 0.0)
            {
                kept.RemoveAt(loserIndex);
            }
            else
            {
                kept[loserIndex] = loser with { OperatingRate = remaining };
            }
        }

        return kept;
    }

    /// <summary>Which half of the pair yields: a declared fixed winner, else the higher skill level, else the
    /// pair's declared tie winner, else the lower uptime. The same ladder the overlay uses, minus its
    /// "applied later" tiebreak, which has no meaning once applications are merged into a rate.</summary>
    private static int LoserOf(DataManager.ExclusiveBuffPair pair, MetricBuffInput a, MetricBuffInput b)
    {
        if (pair.FixedWinner != 0)
        {
            return pair.FixedWinner == pair.A ? pair.B : pair.A;
        }

        if (a.Level > 0 && b.Level > 0 && a.Level != b.Level)
        {
            return a.Level > b.Level ? pair.B : pair.A;
        }

        if (pair.TieWinner != 0)
        {
            return pair.TieWinner == pair.A ? pair.B : pair.A;
        }

        return a.OperatingRate >= b.OperatingRate ? pair.B : pair.A;
    }

    /// <summary>The loser's uptime with the winner's covered time taken out, as a percentage.</summary>
    private static double ExclusiveRemainder(MetricBuffInput loser, MetricBuffInput winner)
    {
        if (loser.Spans is { Count: > 0 } loserSpans && winner.Spans is { Count: > 0 } winnerSpans)
        {
            long covered = loserSpans.Sum(sp => sp.End - sp.Start);
            if (covered <= 0) return 0.0;

            long overlap = BuffUptime.IntersectionMs(loserSpans, winnerSpans);
            return loser.OperatingRate * Math.Clamp((covered - overlap) / (double)covered, 0.0, 1.0);
        }

        // No intervals (an old saved battle, or a caller that only had rates): subtract the rates. Exact when
        // the winner's window sits inside the loser's, and never over-credits otherwise.
        return Math.Max(0.0, loser.OperatingRate - winner.OperatingRate);
    }

    /// <summary>The multiplicative damage gain one buff is worth, from its uptime and its effect values.
    /// Effects compose multiplicatively and each is capped on its own, exactly as the site does it.
    /// <para>Public because the combat detail prices a single row with it — "this buff was worth +15%" — using
    /// the same arithmetic the totals came from, so a row and the summary can never disagree.</para></summary>
    public static double Gain(MetricBuffInput buff, BuffValueCatalog catalog)
    {
        double uptime = Math.Clamp(buff.OperatingRate, 0.0, 100.0) / 100.0;
        if (uptime <= 0.0)
        {
            return 0.0;
        }

        // Level-priced synergy first; the shipped snapshot only fills in what the catalog does not model.
        IReadOnlyList<BuffGainEffect> effects =
            PartySynergyCatalog.Effects(buff.DisplayBase, buff.Level)
            ?? Snapshot(buff, catalog);

        double multiplier = 1.0;
        foreach (BuffGainEffect effect in effects)
        {
            double raw = effect.Category switch
            {
                // A resistance only helps when it was stripped OFF THE BOSS. The same category on a player is
                // that player's own survivability and moves no damage.
                BuffGainCategory.Defense => buff.BossScope && effect.Value < 0 ? Math.Abs(effect.Value) / 100.0 : 0.0,
                BuffGainCategory.None => 0.0,
                _ => effect.Value / 100.0,
            };

            double gain = Math.Clamp(raw * uptime, 0.0, MaxSingleEffectGain);
            if (gain > 0.0)
            {
                multiplier *= 1.0 + gain;
            }
        }

        return Math.Max(0.0, multiplier - 1.0);
    }

    /// <summary>Snapshot lookup: the exact runtime code first, then the display base — the site's table is
    /// keyed by runtime code and a rank the snapshot predates (질풍의 권능's rank-5, 불패의 진언's rank-5) has
    /// no row of its own.</summary>
    private static IReadOnlyList<BuffGainEffect> Snapshot(MetricBuffInput buff, BuffValueCatalog catalog)
    {
        IReadOnlyList<BuffGainEffect> direct = catalog.Get(buff.Code);
        return direct.Count > 0 ? direct : catalog.Get(buff.DisplayBase);
    }
}

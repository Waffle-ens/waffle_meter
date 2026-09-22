using WaffleMeter.Capture;

namespace WaffleMeter.Data;

/// <summary>
/// What is known about the current 시련 run's difficulty. The party sets four knobs (each 1~4) before
/// entering and the game shows their SUM, so the level runs 4~16 — and every level shares one map and one set
/// of boss codes, which is why it has to be read off the wire at all.
/// <para>All four are readable now: the room snapshot's <c>_affix_list</c> carries every knob, so a run whose
/// room the meter saw gets a NUMBER. The range remains for runs where it did not — joining someone else's
/// room, automatch — and reporting a range still beats reporting a guess: pooling a 4 with a 16 distorts a
/// percentile badly (the boss has 2.2x the HP), but so would filing a run under the wrong level.</para>
/// </summary>
public readonly record struct TrialDifficulty(
    int? Timelimit, int? Rebirthlimit, int? BossBuff, int? SkillUpgrade, int MapId = TrialDungeonAxes.BakronIslandMapId)
{
    private int[] Levels => [Timelimit ?? 0, Rebirthlimit ?? 0, BossBuff ?? 0, SkillUpgrade ?? 0];

    /// <summary>This run's per-axis ceilings. 바크론 is the default so every existing construction site and
    /// test keeps its exact meaning.</summary>
    private int[] AxisMax => TrialDungeonAxes.For(MapId);

    /// <summary>How many of the four knobs are known.</summary>
    public int KnownCount => Levels.Count(l => l > 0);

    /// <summary>True once anything at all has been observed — i.e. this IS a trial run.</summary>
    public bool IsTrial => KnownCount > 0;

    /// <summary>The displayed level, or null while any knob is still unknown.</summary>
    public int? Level => KnownCount == TrialAffixCatalog.GroupCount ? Levels.Sum() : null;

    /// <summary>Lowest displayed level consistent with what is known (unknown knobs assumed 1).</summary>
    public int LevelMin => Levels.Sum(l => l > 0 ? l : 1);

    /// <summary>Highest displayed level consistent with what is known (unknown knobs assumed at their
    /// ceiling).
    /// <para>⚠️ The ceiling is per-axis, not a flat 4. 불의 신전's knobs top out at 3/3/8/2, so an unread
    /// 보스 강화 there widens the range by 7 where 바크론 widens it by 3 — a range label from one dungeon
    /// says nothing about how coarse the other's is.</para></summary>
    public int LevelMax
    {
        get
        {
            int[] max = AxisMax;
            int[] levels = Levels;
            int sum = 0;
            for (int i = 0; i < levels.Length; i++)
            {
                sum += levels[i] > 0 ? levels[i] : max[i];
            }

            return sum;
        }
    }

    /// <summary>"시련 16단계" when the level is pinned, "시련 13~16단계" while it isn't, "" when this is not
    /// a trial run at all.</summary>
    public string Label =>
        !IsTrial ? string.Empty
        : Level is { } exact ? $"시련 {exact}단계"
        : $"시련 {LevelMin}~{LevelMax}단계";

    /// <summary>
    /// The three axes that change how much damage a run can put out.
    /// <para>보스 강화 4 raises the boss's max HP 120%, its damage amplification 50%, its combat speed 40%
    /// and its groggy gauge 50%; 바크론 패턴 강화 4 adds 가시 속박, more 덩굴, and 탄환초 소환. A percentile
    /// that mixed those with lower settings would not be measuring anything.</para>
    /// <para>⚠️ 부활 제한 is deliberately NOT here even though it is readable now. This predicate is
    /// the one older builds could evaluate, and widening it would silently retire every run they can still
    /// file. Ask <see cref="IsTop16Difficulty"/> when you mean the number on the screen.</para>
    /// </summary>
    /// <para>⚠️ "4" became "this axis's ceiling" when 불의 신전 arrived with 3/3/8/2. For 바크론 the two
    /// read identically, so no shipped run changes verdict; what it does NOT do is widen the predicate —
    /// 부활 제한 is still excluded.</para>
    public bool IsTopDifficulty =>
        Timelimit == AxisMax[(int)TrialAffixGroup.Timelimit]
        && BossBuff == AxisMax[(int)TrialAffixGroup.BossBuff]
        && SkillUpgrade == AxisMax[(int)TrialAffixGroup.BakronSkillUpgrade];

    /// <summary>
    /// 16단계 — all four knobs at 4, i.e. the level the screen shows.
    /// <para>부활 제한 joined the readable knobs once the room's <c>_affix_list</c> was decoded. It leaves
    /// the boss alone, so it is not a damage axis; what it buys is that "top" means here exactly what it
    /// means on the screen. Kept SEPARATE from <see cref="IsTopDifficulty"/> rather than folded into it —
    /// the statistics site made the same split (a second axis set was ADDED beside the existing one, not
    /// substituted for it), and the two repos must not drift on what a name promises.</para>
    /// <para>⚠️ 86 observed rooms put the sum at 16 only 42% of the time, so this is a real filter, not a
    /// formality.</para>
    /// </summary>
    public bool IsTop16Difficulty =>
        IsTopDifficulty && Rebirthlimit == AxisMax[(int)TrialAffixGroup.Rebirthlimit];
}

/// <summary>
/// Per-dungeon affix ceilings. Every 시련 shows the SUM of four knobs and every one of them runs 4~16, but
/// the knobs do not divide the range the same way: 바크론 is 4/4/4/4 while 불의 신전 is 3/3/8/2 (confirmed
/// against <c>DungeonTrial.dat</c> and cross-checked by the l10n achievement "보스 강화 특성을 8단계로 클리어").
/// <para>🔴 <b>ADD rows here; never edit one.</b> A dungeon's ceilings decide what
/// <see cref="TrialDifficulty.IsTopDifficulty"/> promises for every build already in the wild, and the
/// statistics site keys its comparability gate off the same numbers. Changing 바크론's row would silently
/// re-file runs that shipped builds can still upload; adding a row cannot.</para>
/// <para>The four slots are positional — the wire (<c>0x9702</c> tail <c>_affix_list</c>) carries an array,
/// never an axis name. Slot 3 is "the fourth knob", which is 바크론 패턴 강화 in one dungeon and
/// <c>Pc_debuff_1</c> in the other; the payload keeps calling it <c>skillUpgrade</c> because the name is a
/// slot label, not a claim about meaning. Axis identity is a function of (map, slot) and the web derives it
/// from the map id.</para>
/// </summary>
public static class TrialDungeonAxes
{
    /// <summary>바크론의 공중섬 시련.</summary>
    public const int BakronIslandMapId = 600074;

    /// <summary>불의 신전 시련 (2026-09-23, client 112). ⚠️ 600025, NOT 600024 — the dungeon's
    /// 탐험/보통/어려움 are 600021/22/23 and the trial skips a number.</summary>
    public const int FireTempleMapId = 600025;

    private static readonly int[] BakronMax = [4, 4, 4, 4];
    private static readonly int[] FireTempleMax = [3, 3, 8, 2];

    /// <summary>True when this map is a 시련 whose knobs the meter knows how to read.</summary>
    public static bool IsTrialMap(int mapId) =>
        mapId == BakronIslandMapId || mapId == FireTempleMapId;

    /// <summary>The map's per-axis ceilings, in <see cref="TrialAffixGroup"/> order. Unknown maps fall back
    /// to 바크론's so a caller that skipped <see cref="IsTrialMap"/> degrades to the old behaviour rather
    /// than throwing.</summary>
    public static int[] For(int mapId) =>
        mapId == FireTempleMapId ? FireTempleMax : BakronMax;

    /// <summary>The ceiling for one axis, or 0 when the axis index is out of range.</summary>
    public static int Max(int mapId, int axis)
    {
        int[] m = For(mapId);
        return axis >= 0 && axis < m.Length ? m[axis] : 0;
    }
}

/// <summary>
/// Collects the difficulty knobs observed for the current instance. Pure and lock-guarded so the packet
/// thread can write while the UI reads.
/// <para>Reset on entering a new instance: the knobs are chosen per run, so carrying them across would file
/// the next run under the previous one's difficulty.</para>
/// </summary>
public sealed class TrialDifficultyTracker
{
    /// <summary>바크론's map, kept under its original name for callers that predate the second trial.
    /// Ask <see cref="TrialDungeonAxes.IsTrialMap"/> when you mean "is this a 시련".</summary>
    public const int TrialMapId = TrialDungeonAxes.BakronIslandMapId;

    /// <summary>The instance phase whose window IS the 제한 시간 setting. Phases 3/4 are short transitions
    /// (measured ~10 s) and must not be mistaken for it.</summary>
    private const int MainPhase = 2;

    private readonly object _gate = new();
    private readonly int?[] _levels = new int?[TrialAffixCatalog.GroupCount];
    private long _runStartMs;

    /// <summary>Which 시련 the held knobs belong to. Decides their ceilings, so it has to travel with them —
    /// a 3 means "top" in 불의 신전's 제한 시간 and "not top" in 바크론's.</summary>
    private int _mapId = TrialDungeonAxes.BakronIslandMapId;

    /// <summary>어픽스를 실어 온 방. <b>이것이 런 토큰이다.</b> 런마다 반드시 새 key 가 발급되고(실측 9/9:
    /// 07-23 384095/384441/384766/385001, 08-05 132887/133388/133923) 같은 방에서 재풀하면 유지된다.</summary>
    private int _roomKey;

    /// <summary>
    /// 방 스냅샷(0x9702)이 실어 온 어픽스 네 축. <b>네 축이 다 오므로 이 경로만으로 단계가 점값이 된다</b> —
    /// 종전의 "시련 13~16단계" 범위 라벨은 부활 제한을 읽을 방법이 없어서 나온 것이었다.
    /// <para>어픽스는 마을의 <b>방</b>에서 오고 인스턴스 phase-1 보다 3.4~4.3초 <b>먼저</b> 도착한다(10/10).
    /// 그래서 런 경계는 인스턴스가 아니라 방이 긋는다.</para>
    /// </summary>
    public void ObserveRoomAffixes(int dungeonId, int roomKey, int[] levels)
    {
        if (levels is null || levels.Length != TrialAffixCatalog.GroupCount)
        {
            return;
        }

        if (!TrialDungeonAxes.IsTrialMap(dungeonId))
        {
            // 시련이 아닌 방 정보를 받았다 = 그 사이 다른 컨텐츠를 잡았다는 뜻이고, 들고 있던 어픽스는
            // 지난 런 것이다. 맵 전환 Reset 과 같은 이유다.
            Reset();
            return;
        }

        lock (_gate)
        {
            if (roomKey != _roomKey || dungeonId != _mapId)
            {
                // 던전이 바뀌면 방 key 가 같아도 지운다 — 축 상한이 달라서 이전 런의 값을 새 던전의
                // 자로 재면 엉뚱한 단계가 나온다.
                Array.Clear(_levels);
                _roomKey = roomKey;
            }

            _mapId = dungeonId;

            for (int i = 0; i < TrialAffixCatalog.GroupCount; i++)
            {
                // ⚠️ 상한은 던전별이다. 종전의 고정 `<= 4` 는 불의 신전의 보스 강화 5~8 을 **조용히
                // 버렸다** — 그 던전에서 16단계는 보스 강화 8 이므로, 정확히 최상위 런만 통째로
                // 미기록되는 형태로 틀린다.
                if (levels[i] >= 1 && levels[i] <= TrialDungeonAxes.Max(dungeonId, i))
                {
                    _levels[i] = levels[i];
                }
            }
        }
    }

    public void Observe(TrialAffixGroup group, int level)
    {
        if (level < 1)
        {
            return;
        }

        lock (_gate)
        {
            // 상한 검사는 락 안에서 한다 — 어느 던전의 자로 재는지가 _mapId 에 달렸다.
            if (level > TrialDungeonAxes.Max(_mapId, (int)group))
            {
                return;
            }

            _levels[(int)group] = level;
        }
    }

    /// <summary>Feed an instance phase window. Only the trial's main phase says anything about difficulty;
    /// everything else is ignored rather than guessed at.
    /// <para>이 창은 <b>런 경계를 긋지 않는다</b> — 그건 <see cref="ObserveRoomAffixes"/> 의 roomKey 가 한다.
    /// 여기서 하는 일은 제한 시간 축을 채우는 것뿐이고, 어픽스를 못 받은 런에서는 그게 유일한 소스다.</para></summary>
    public void ObservePhaseWindow(int mapId, int phase, long startMs, long windowMs)
    {
        if (mapId <= 0)
        {
            return;
        }

        if (!TrialDungeonAxes.IsTrialMap(mapId))
        {
            // A phase window for ANOTHER map is proof the trial is over, and it is the only such proof the
            // meter gets — there is no "you left the instance" packet. Until 2026-08-11 this method returned
            // here without clearing, so the knobs outlived the run: one 시련 at the start of a session relabelled
            // every dungeon after it, and 돌아온 추방자 가르가움 — a 초월 2단계 boss — rendered as
            // "(시련 13~16단계)" for the rest of the evening.
            //
            // Deliberately NOT symmetrical: entering the trial's map must not clear, because this window is not
            // ordered against the affix broadcasts and clearing on arrival would discard settings that got here
            // first. Leaving has no such hazard — everything held is the old run's by definition.
            Reset();
            return;
        }

        if (phase != MainPhase)
        {
            return;
        }

        lock (_gate)
        {
            // 🔴 방을 본 런에서는 창이 런 경계를 긋지 않는다. 어픽스는 마을 방에서 오고 phase-1 보다
            // 3.4~4.3초 먼저 도착하므로(실측 10/10), 창이 지우면 방에서 이미 받아 둔 네 축이 인스턴스에
            // 들어가는 순간 통째로 날아간다 — 그게 "네 축이 다 와도 라벨이 여전히 범위"였던 이유다.
            // 같은 방 재풀도 마찬가지다: key 가 유지되고 값도 같은데(07-15 room 187978, phase 1→2→3→4→1→2)
            // 창은 startMs 가 바뀌었다고 지워 버린다.
            //
            // ⚠️ 방을 못 본 런에서는 종전 동작을 그대로 남긴다. 어보노멀 경로만으로 축을 채운 경우
            // (남의 방 초대·자동매칭) 창 말고는 런 경계를 알 방법이 없고, 마을은 인스턴스가 아니라
            // '다른 맵 창' Reset 도 안 걸린다 — 그때 안 지우면 이전 런의 보스 강화가 다음 런에 샌다.
            if (_roomKey == 0 && _runStartMs != 0 && startMs != _runStartMs)
            {
                Array.Clear(_levels);
            }

            _runStartMs = startMs;
            _mapId = mapId;
        }

        // ⚠️ 초 → 단계 환산표(1800/1200/900/600)는 바크론에서 실측한 것이고, 불의 신전의 제한 시간은
        // 축 상한이 3이라 같은 표일 수 없다. 모르는 표로 환산하느니 이 축은 비워 둔다 — 불의 신전은
        // 방 스냅샷이 네 축을 다 실어 오므로(_affix_list) 이 경로 없이도 점값이 나온다.
        // 환산표를 실측하면 여기 던전별로 갈라 붙일 것.
        if (mapId != TrialDungeonAxes.BakronIslandMapId)
        {
            return;
        }

        int level = TrialAffixCatalog.TimelimitLevelForSeconds(windowMs / 1000);
        if (level > 0)
        {
            Observe(TrialAffixGroup.Timelimit, level);
        }
    }

    public TrialDifficulty Current
    {
        get
        {
            lock (_gate)
            {
                return new TrialDifficulty(
                    Timelimit: _levels[(int)TrialAffixGroup.Timelimit],
                    Rebirthlimit: _levels[(int)TrialAffixGroup.Rebirthlimit],
                    BossBuff: _levels[(int)TrialAffixGroup.BossBuff],
                    SkillUpgrade: _levels[(int)TrialAffixGroup.BakronSkillUpgrade],
                    MapId: _mapId);
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            Array.Clear(_levels);
            _runStartMs = 0;
            _roomKey = 0;
            _mapId = TrialDungeonAxes.BakronIslandMapId;
        }
    }
}

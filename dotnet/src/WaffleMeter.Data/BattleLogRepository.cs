namespace WaffleMeter.Data;

/// <summary>
/// Ported from Kotlin BattleLogRepository: a bounded history of saved battles (cap 30 — the history panel
/// shows them newest-first in a scrollable list). A new log that matches an existing battle (same target id
/// + mob code, within a 120s gap) replaces it with the "preferred" of the two (higher damage, but keep the
/// existing one if the new is a longer idle-extended run with ~no extra damage). Order preserved (oldest first).
/// </summary>
public sealed class BattleLogRepository
{
    private const long SameBattleMergeWindowMs = 120_000L;
    private const long IdleExtensionGraceMs = 30_000L;
    private const int MaxSize = 30;

    private readonly List<DpsLog> _storage = [];

    public void Save(DpsLog data)
    {
        int existingIndex = _storage.FindLastIndex(it => IsSameBattle(it, data));
        if (existingIndex >= 0)
        {
            _storage[existingIndex] = SelectPreferred(_storage[existingIndex], data);
            return;
        }

        if (_storage.Count >= MaxSize)
        {
            _storage.RemoveAt(0);
        }

        _storage.Add(data);
    }

    public DpsLog? Get(int idx) => idx >= 0 && idx < _storage.Count ? _storage[idx] : null;

    public IReadOnlyList<DpsLog> GetAll() => _storage;

    public void Flush() => _storage.Clear();

    private static bool IsSameBattle(DpsLog a, DpsLog b)
    {
        MobInfo? aTarget = a.Report.Target;
        MobInfo? bTarget = b.Report.Target;
        if (aTarget == null || bTarget == null)
        {
            return false;
        }

        if (aTarget.Id != bTarget.Id)
        {
            return false;
        }

        if (aTarget.Mob.Code != bTarget.Mob.Code)
        {
            return false;
        }

        if (a.Report.BattleStart <= 0L || b.Report.BattleStart <= 0L)
        {
            return false;
        }

        long aEnd = a.Report.BattleEnd >= a.Report.BattleStart ? a.Report.BattleEnd : a.Report.BattleStart;
        long bEnd = b.Report.BattleEnd >= b.Report.BattleStart ? b.Report.BattleEnd : b.Report.BattleStart;
        long gap = aEnd < b.Report.BattleStart ? b.Report.BattleStart - aEnd
            : bEnd < a.Report.BattleStart ? a.Report.BattleStart - bEnd
            : 0L;

        return gap <= SameBattleMergeWindowMs;
    }

    private static DpsLog SelectPreferred(DpsLog existing, DpsLog next)
    {
        double existingDamage = TotalDamage(existing);
        double nextDamage = TotalDamage(next);
        long existingDuration = Duration(existing);
        long nextDuration = Duration(next);

        if (nextDamage <= existingDamage * 1.01 && nextDuration > existingDuration + IdleExtensionGraceMs)
        {
            return existing;
        }

        return nextDamage + 0.001 >= existingDamage ? next : existing;
    }

    private static double TotalDamage(DpsLog log) => log.Report.Information.Values.Sum(i => i.Amount);

    private static long Duration(DpsLog log) => Math.Max(log.Report.BattleEnd - log.Report.BattleStart, 0L);
}

using WaffleMeter.Capture;

namespace WaffleMeter.Data;

/// <summary>A read window over a target's packet ring buffer (Kotlin PacketRepository.PacketWindow).</summary>
public sealed record PacketWindow(
    IReadOnlyList<ParsedDamagePacket> Packets, long NextSequence, bool DroppedBeforeStart, int TotalSize);

/// <summary>
/// Verbatim port of Kotlin PacketRepository: per-target ring buffer of damage packets (cap 150k,
/// initial 1024, x2 growth, overwrite-oldest when full) + the current-target/battle-time state.
/// </summary>
public sealed class PacketRepository
{
    public const int MaxPacketsPerTarget = 150_000;
    private const int InitialBufferCapacity = 1_024;

    private sealed class RingBuffer(int maxCapacity)
    {
        private ParsedDamagePacket?[] _buffer = new ParsedDamagePacket?[Math.Min(InitialBufferCapacity, maxCapacity)];
        private int _start;
        private int _size;
        private long _totalAdded;

        public void Add(ParsedDamagePacket packet)
        {
            EnsureCapacityForAppend();
            if (_size < _buffer.Length)
            {
                _buffer[(_start + _size) % _buffer.Length] = packet;
                _size++;
            }
            else
            {
                _buffer[_start] = packet;
                _start = (_start + 1) % _buffer.Length;
            }

            _totalAdded++;
        }

        public List<ParsedDamagePacket> Snapshot()
        {
            var result = new List<ParsedDamagePacket>(_size);
            for (int i = 0; i < _size; i++)
            {
                result.Add(ElementAtOffset(i));
            }

            return result;
        }

        public PacketWindow WindowFrom(long sequence)
        {
            long firstSequence = _totalAdded - _size;
            long safeSequence = Math.Min(Math.Max(sequence, firstSequence), _totalAdded);
            int count = (int)(_totalAdded - safeSequence);
            var result = new List<ParsedDamagePacket>(count);
            int firstOffset = (int)(safeSequence - firstSequence);
            for (int i = 0; i < count; i++)
            {
                result.Add(ElementAtOffset(firstOffset + i));
            }

            return new PacketWindow(result, _totalAdded, sequence < firstSequence, _size);
        }

        private void EnsureCapacityForAppend()
        {
            if (_size < _buffer.Length || _buffer.Length >= maxCapacity)
            {
                return;
            }

            int newCapacity = Math.Min(_buffer.Length * 2, maxCapacity);
            var newBuffer = new ParsedDamagePacket?[newCapacity];
            for (int i = 0; i < _size; i++)
            {
                newBuffer[i] = ElementAtOffset(i);
            }

            _buffer = newBuffer;
            _start = 0;
        }

        private ParsedDamagePacket ElementAtOffset(int offset) => _buffer[(_start + offset) % _buffer.Length]!;
    }

    private readonly Dictionary<int, RingBuffer> _storage = new();
    private int _currentTarget;
    private long _currentBattleStart;
    private long _currentBattleEnd;

    public void Save(ParsedDamagePacket pdp)
    {
        if (!_storage.TryGetValue(pdp.TargetId, out RingBuffer? ring))
        {
            ring = new RingBuffer(MaxPacketsPerTarget);
            _storage[pdp.TargetId] = ring;
        }

        ring.Add(pdp);
    }

    public List<ParsedDamagePacket>? Get(int id) => _storage.TryGetValue(id, out RingBuffer? r) ? r.Snapshot() : null;

    public PacketWindow GetWindow(int id, long sequence) =>
        _storage.TryGetValue(id, out RingBuffer? r) ? r.WindowFrom(sequence) : new PacketWindow([], sequence, false, 0);

    public bool Exist(int id) => _storage.ContainsKey(id);

    public void Flush()
    {
        _currentTarget = 0;
        _currentBattleStart = 0;
        _currentBattleEnd = 0;
        _storage.Clear();
    }

    public int CurrentTarget() => _currentTarget;

    public int CurrentTarget(int targetId)
    {
        int past = _currentTarget;
        _currentTarget = targetId;
        return past;
    }

    public void FlushBattleTime()
    {
        _currentBattleStart = 0;
        _currentBattleEnd = 0;
    }

    public long CurrentBattleStart() => _currentBattleStart;
    public long CurrentBattleEnd() => _currentBattleEnd;

    public void SaveCurrentBattleStart(long time)
    {
        _currentBattleStart = time;
        _currentBattleEnd = 0;
    }

    public void SaveCurrentBattleEnd(long time) => _currentBattleEnd = time;
}

/// <summary>Kotlin MobIdRepository: instanceId -> (mobCode, maxHp).</summary>
public sealed class MobIdRepository
{
    public sealed class MobInstance(int code, int maxHp = 0)
    {
        public int Code { get; } = code;
        public int MaxHp { get; set; } = maxHp;
    }

    private readonly Dictionary<int, MobInstance> _storage = new();

    public void Save(int key, int code)
    {
        int maxHp = _storage.TryGetValue(key, out MobInstance? existing) && existing.Code == code ? existing.MaxHp : 0;
        _storage[key] = new MobInstance(code, maxHp);
    }

    public bool SaveMaxHp(int key, int maxHp)
    {
        if (!_storage.TryGetValue(key, out MobInstance? instance))
        {
            return false;
        }

        if (maxHp > instance.MaxHp)
        {
            instance.MaxHp = maxHp;
        }

        return true;
    }

    public MobInstance? Get(int id) => _storage.TryGetValue(id, out MobInstance? m) ? m : null;
    public bool Exist(int id) => _storage.ContainsKey(id);
    public void Flush() => _storage.Clear();
}

/// <summary>Kotlin MobHpRepository: instanceId -> remaining HP.</summary>
public sealed class MobHpRepository
{
    private readonly Dictionary<int, int> _storage = new();
    public int? Get(int key) => _storage.TryGetValue(key, out int v) ? v : null;
    public void Set(int key, int value) => _storage[key] = value;
    public void Flush() => _storage.Clear();
}

/// <summary>Kotlin SummonRepository: summonId -> owner.</summary>
public sealed class SummonRepository
{
    private readonly Dictionary<int, int> _storage = new();
    public void Save(int summonId, int summonerId) => _storage[summonId] = summonerId;
    public int? Get(int summonId) => _storage.TryGetValue(summonId, out int v) ? v : null;
    public IReadOnlyDictionary<int, int> GetAll() => _storage;
    public void Flush() => _storage.Clear();
}

/// <summary>Kotlin UseBuffRepository: actor/target -> applied buff intervals.</summary>
public sealed class UseBuffRepository
{
    private readonly Dictionary<int, List<UseBuff>> _storage = new();

    public void Save(int id, UseBuff useBuff)
    {
        if (!_storage.TryGetValue(id, out List<UseBuff>? list))
        {
            list = [];
            _storage[id] = list;
        }

        list.Add(useBuff);
    }

    public List<UseBuff> FindOverlapping(int id, long timestamp1, long timestamp2)
    {
        if (!_storage.TryGetValue(id, out List<UseBuff>? list))
        {
            return [];
        }

        return list.Where(b => b.BuffStart <= timestamp2 && b.BuffEnd >= timestamp1).ToList();
    }

    public void PruneBefore(long timestamp)
    {
        foreach (int key in _storage.Keys.ToList())
        {
            List<UseBuff> buffs = _storage[key];
            buffs.RemoveAll(b => b.BuffEnd < timestamp);
            if (buffs.Count == 0)
            {
                _storage.Remove(key);
            }
        }
    }

    public void Flush() => _storage.Clear();
}

/// <summary>Skill catalog (Kotlin SkillRepository): code -> Skill (with name).</summary>
public sealed class SkillRepository
{
    private readonly Dictionary<long, Skill> _storage = new();
    public void Save(long key, Skill value) => _storage[key] = value;
    public Skill? Get(long key) => _storage.TryGetValue(key, out Skill? s) ? s : null;
    public bool Exist(long key) => _storage.ContainsKey(key);
}

/// <summary>Buff catalog (Kotlin BuffRepository): code -> Buff.</summary>
public sealed class BuffRepository
{
    private readonly Dictionary<int, Buff> _storage = new();
    public void Save(Buff value) => _storage[value.Code] = value;
    public Buff? Get(int code) => _storage.TryGetValue(code, out Buff? b) ? b : null;
}

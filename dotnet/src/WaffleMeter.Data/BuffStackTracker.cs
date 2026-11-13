namespace WaffleMeter.Data;

/// <summary>
/// 본인 버프의 <b>중첩 수</b>를 인스턴스(slot) 수로 센다. 최대 중첩 수가 카탈로그(<c>buff_catalog.json</c>의
/// <c>"s"</c>)에 적힌 표시 base만 여기 들어온다 — 그 밖의 버프는 종전처럼 base 하나에 slot 하나다.
/// <para><b>모델(코퍼스 실측).</b> 서버는 중첩 하나를 버프 인스턴스 하나로 보낸다. 새 중첩은 새 slot의 0x382A와
/// 기존 slot 전부의 0x382B가 같은 틱에 오고, 최대 중첩에서 다시 걸리면 0x382B가 가장 오래된 slot을 덮어써서 수가
/// 변하지 않는다. 해제는 0x382C가 slot을 지목한다. 그래서 "같은 raw 코드로 살아 있는 slot의 수"가 곧 중첩 수다.
/// 와이어의 <c>_multiply_value</c>는 두 로그 7,328프레임에서 한 번도 실리지 않아 쓰지 않는다.</para>
/// <para><b>raw 코드가 같을 때만 센다.</b> 같은 표시 base로 접히는 서로 다른 효과가 서로의 수를 부풀리지 않게
/// 하려는 것이다. 대표 raw는 마지막으로 기록된 코드다.</para>
/// <para>slot 0(모름)은 세지 않는다. 결과는 1 이상, 최대 중첩 수 이하로 자른다.</para>
/// <para>스레드 안전하지 않다 — <c>DataManager</c>가 <c>_ownerBuffGate</c> 안에서만 부른다.</para>
/// </summary>
public sealed class BuffStackTracker
{
    private readonly Dictionary<int, (int Raw, Dictionary<int, (int Raw, long End)> Slots)> _bases = new();
    private int _owner;

    /// <summary>slot 하나를 기록한다(새 중첩이거나 기존 slot의 갱신). 이미 만료된 slot은 이때 함께 버린다.
    /// <para><paramref name="ownerUid"/>가 앞서 기록한 주인과 다르면 전부 비우고 시작한다 — 본인은 존을 넘을 때마다
    /// 새 uid로 재등록되는데, 옛 uid의 slot을 지목하는 0x382C는 executor 게이트에 막혀 우리에게 닿지 않는다.
    /// 그대로 두면 옛 slot이 만료까지 남아 수를 부풀린다.</para></summary>
    public void Record(int ownerUid, int baseCode, int raw, int slot, long end, long nowMs)
    {
        if (ownerUid != _owner)
        {
            _bases.Clear();
            _owner = ownerUid;
        }

        if (!_bases.TryGetValue(baseCode, out var state))
        {
            state = (raw, new Dictionary<int, (int Raw, long End)>());
        }

        Prune(state.Slots, nowMs);
        if (slot != 0)
        {
            state.Slots[slot] = (raw, end);
        }

        _bases[baseCode] = (raw, state.Slots);
    }

    /// <summary>0x382C가 지목한 slot을 뺀다. 이 base의 대표 raw로 아직 살아 있는 slot이 남으면 그중 만료가 가장 늦은
    /// (slot, End)를 돌려주고, 남은 게 없으면 null이다. 지목된 slot이 이 base에 없었으면 <paramref name="touched"/>가
    /// false다.</summary>
    public (int Slot, long End)? Remove(int baseCode, IReadOnlyList<int> slots, long nowMs, out bool touched)
    {
        touched = false;
        if (!_bases.TryGetValue(baseCode, out var state))
        {
            return null;
        }

        foreach (int s in slots)
        {
            touched |= state.Slots.Remove(s);
        }

        Prune(state.Slots, nowMs);
        return Latest(state, nowMs);
    }

    /// <summary>대표 raw로 살아 있는 slot 중 만료가 가장 늦은 것. 없으면 null.</summary>
    public (int Slot, long End)? Latest(int baseCode, long nowMs) =>
        _bases.TryGetValue(baseCode, out var state) ? Latest(state, nowMs) : null;

    /// <summary>중첩 수. 기록이 없거나 살아 있는 slot이 없으면 1(아이콘이 떠 있는 한 최소 한 겹이다).
    /// <paramref name="max"/>가 양수면 그 값에서 자른다.</summary>
    public int Count(int baseCode, long nowMs, int max)
    {
        int n = 0;
        if (_bases.TryGetValue(baseCode, out var state))
        {
            foreach ((int raw, long end) in state.Slots.Values)
            {
                if (raw == state.Raw && end > nowMs)
                {
                    n++;
                }
            }
        }

        n = Math.Max(1, n);
        return max > 0 ? Math.Min(n, max) : n;
    }

    /// <summary>사망·신원 변경·하드 리셋. 다음 적용은 1부터 다시 센다.</summary>
    public void Clear() => _bases.Clear();

    private static (int Slot, long End)? Latest((int Raw, Dictionary<int, (int Raw, long End)> Slots) state, long nowMs)
    {
        (int Slot, long End)? best = null;
        foreach (KeyValuePair<int, (int Raw, long End)> kv in state.Slots)
        {
            if (kv.Value.Raw == state.Raw && kv.Value.End > nowMs && (best is null || kv.Value.End > best.Value.End))
            {
                best = (kv.Key, kv.Value.End);
            }
        }

        return best;
    }

    private static void Prune(Dictionary<int, (int Raw, long End)> slots, long nowMs)
    {
        foreach (int dead in slots.Where(kv => kv.Value.End <= nowMs).Select(kv => kv.Key).ToList())
        {
            slots.Remove(dead);
        }
    }
}

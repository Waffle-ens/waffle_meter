using WaffleMeter.Data;
using WaffleMeter.Services;

namespace WaffleMeter.App.Core;

/// <summary>
/// 스킬 쿨타임 오버레이에 띄울 스킬 선택, 재시작 후에도 유지된다.
///
/// <para><b>저장하는 것은 여집합이다.</b> <c>cooldownUi.hidden</c> 에는 사용자가 <b>끈</b> 코드가 들어간다.
/// 켠 목록을 저장하는 쪽이 자연스러워 보이지만 함정이고, 그 함정은 이 저장소가 이미 한 번 밟았다
/// (<see cref="SkillVisibility"/> 의 주석 참고). 기본값이 "전부 표시"인데 켠 목록으로는 그 기본값을 표현할
/// 방법이 없다.</para>
/// <list type="bullet">
///   <item>전체 해제가 빈 문자열로 직렬화되면, 되읽을 때 "픽커를 한 번도 안 열었다"와 구분되지 않는다.
///   여집합이면 빈 값은 "숨긴 것 없음"이고 키가 아예 없는 것과 뜻이 같아 모호함이 사라진다.</item>
///   <item>패치로 스킬이 늘어도 아무의 숨김 목록에도 없으므로 자동으로 보인다. 켠 목록이었다면 "저장 당시
///   알던 코드" 키를 따로 두고 매 로드마다 차집합을 떠야 같은 자리에 도달한다.</item>
/// </list>
///
/// <para>구 키 마이그레이션이 없다 — 이 키는 새로 생겼고 이전 의미를 가진 값이 세상에 없다. 그래서
/// <see cref="SkillVisibility"/> 의 변환·구제 로직을 물려받지 않는다.</para>
/// </summary>
public sealed class CooldownVisibility : ISkillVisibility
{
    private const string Key = "cooldownUi.hidden";

    private readonly PropertyHandler _props;
    private readonly List<int> _all;

    /// <summary>표시할 코드. 픽커에 참조로 넘어가므로 제자리에서만 바꾸고 절대 재대입하지 않는다.
    /// 저장 형식만 여집합이고 메모리 안에서는 전부 "보이는 것"으로 말한다.</summary>
    public HashSet<int> Codes { get; }

    /// <summary>카탈로그가 아는 전 스킬(그룹 대표 base 코드). 여집합 계산의 모수다.</summary>
    public IReadOnlyList<int> AllCodes => _all;

    public CooldownVisibility(PropertyHandler props, CooldownCatalog catalog)
    {
        _props = props;
        _all = catalog.Skills.Select(s => s.BaseCode).OrderBy(c => c).ToList();
        Codes = new HashSet<int>();
        LoadInto(Codes);
    }

    /// <summary>설정 가져오기가 값을 갈아끼운 뒤 부른다. 집합은 <b>제자리에서</b> 갱신한다 — 같은 인스턴스를
    /// 픽커가 들고 있으므로 교체하면 픽커가 옛것을 계속 쓴다.</summary>
    public void Reload()
    {
        var fresh = new HashSet<int>();
        LoadInto(fresh);
        Codes.Clear();
        Codes.UnionWith(fresh);
        Changed?.Invoke();
    }

    /// <summary>바깥에서 집합이 통째로 바뀌었을 때(설정 가져오기). 픽커는 행을 생성자에서 한 번 만들므로
    /// 이 신호가 없으면 옛 상태를 계속 그리다가 사용자가 토글하는 순간 그대로 되쓴다.</summary>
    public event Action? Changed;

    public bool IsVisible(int code) => Codes.Contains(code);

    public void Set(int code, bool visible)
    {
        if (visible ? Codes.Add(code) : Codes.Remove(code))
        {
            Save();
        }
    }

    public void SetMany(IEnumerable<int> codes, bool visible)
    {
        bool changed = false;
        foreach (int code in codes)
        {
            changed |= visible ? Codes.Add(code) : Codes.Remove(code);
        }

        if (changed)
        {
            Save();
        }
    }

    private void LoadInto(HashSet<int> target)
    {
        HashSet<int> hidden = Parse(_props.GetProperty(Key));
        foreach (int code in _all)
        {
            if (!hidden.Contains(code))
            {
                target.Add(code);
            }
        }
    }

    /// <summary>쉼표 구분 코드. 대괄호를 벗기는 이유는 <see cref="SkillVisibility"/> 와 같다 — 사람이 손으로
    /// 넣거나 다른 형식에서 복사해 온 값이 <c>[a,b]</c> 로 오면 ',' 만으로 쪼갤 때 첫·마지막 항목이 조용히
    /// 사라진다.</summary>
    private static HashSet<int> Parse(string? raw)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return set;
        }

        raw = raw.Trim().TrimStart('[').TrimEnd(']');
        foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out int code) && code > 0)
            {
                set.Add(code);
            }
        }

        return set;
    }

    private void Save() =>
        _props.SetProperty(Key, string.Join(",", _all.Where(c => !Codes.Contains(c))));
}

using WaffleMeter.App.Core;
using WaffleMeter.Data;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// 쿨타임 오버레이의 "표시할 스킬 선택" 픽커. 직업별로 일반/스티그마 두 묶음의 토글 칩을 낸다.
/// <para>참가요청 배지 픽커(<see cref="SkillSettingsViewModel"/>)와 <b>행 클래스와 창은 공유</b>하고
/// (<see cref="SkillJobGroupViewModel"/> · <see cref="SkillSettingsFlyout"/>) 카탈로그와 저장 키만 다르다:
/// 저쪽은 컴파일된 167코드 목록과 <c>joinSkills.hidden</c>, 이쪽은 배포 자산에서 읽은 249코드와
/// <c>cooldownUi.hidden</c> 이다. ⚠️ 두 픽커가 같은 <see cref="ISkillVisibility"/> 인스턴스를 쓰면 배지 토글이
/// 쿨타임 표시를 함께 바꾼다 — 집합을 참조로 넘기기 때문이다.</para>
/// </summary>
public sealed class CooldownPickerViewModel
{
    public CooldownPickerViewModel(CooldownCatalog catalog, CooldownVisibility visibility)
    {
        Dictionary<int, string> jobName = SkillCatalog.JobPrefix.ToDictionary(kv => kv.Value, kv => kv.Key);
        var names = new Dictionary<int, string>();

        Groups = catalog.Skills
            .GroupBy(s => s.Job)
            .OrderBy(g => g.Key)
            .Where(g => jobName.ContainsKey(g.Key))
            .Select(g =>
            {
                foreach (CooldownSkillInfo s in g)
                {
                    names[s.BaseCode] = s.Name;
                }

                // 픽커 안의 순서는 오버레이의 슬롯 순서(Order)와 같게 둔다 — 두 화면에서 같은 스킬을 다른
                // 자리에서 찾게 만들 이유가 없다.
                var grouped = new GroupedJobSkills(
                    jobName[g.Key],
                    g.Where(s => !s.IsStigma).OrderBy(s => s.Order).Select(s => s.BaseCode).ToList(),
                    g.Where(s => s.IsStigma).OrderBy(s => s.Order).Select(s => s.BaseCode).ToList());

                return new SkillJobGroupViewModel(
                    grouped, visibility, () => Changed?.Invoke(),
                    code => names.TryGetValue(code, out string? n) ? n : code.ToString());
            })
            .ToList();
    }

    public IReadOnlyList<SkillJobGroupViewModel> Groups { get; }

    /// <summary>토글이 일어난 뒤. App 이 오버레이를 즉시 다시 그리도록 쓴다.</summary>
    public event Action? Changed;

    /// <summary>공유 집합을 바깥에서 갈아끼웠을 때(설정 가져오기) 칩을 다시 읽는다. 알림만 하므로
    /// <see cref="Changed"/> 로 되돌아오지 않는다.</summary>
    public void Refresh()
    {
        foreach (SkillJobGroupViewModel group in Groups)
        {
            group.Refresh();
        }
    }
}

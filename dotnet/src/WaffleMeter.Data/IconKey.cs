namespace WaffleMeter.Data;

/// <summary>
/// 코드 하나를 출하 아이콘 PNG 파일명(코드)으로 바꾸는 규칙. 그리는 쪽(App.Wpf <c>JoinIcons</c>)에는 테스트
/// 프로젝트가 없어 판단만 여기에 둔다 — 어떤 코드들이 PNG를 갖는지는 호출자가 <c>hasIcon</c>으로 넘긴다.
/// </summary>
public static class IconKey
{
    /// <summary>
    /// 스킬·버프 코드의 아이콘 키: 정확한 코드에 PNG가 있으면 그것, 없으면 <see cref="Fallbacks"/>를 차례로 보고
    /// 처음 PNG가 있는 코드, 끝까지 없으면 null.
    /// </summary>
    public static int? Skill(int code, Func<int, bool> hasIcon)
    {
        if (code <= 0)
        {
            return null;
        }

        if (hasIcon(code))
        {
            return code;
        }

        foreach (int candidate in Fallbacks(code))
        {
            if (hasIcon(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 버프 오버레이 슬롯·버프 picker 행의 아이콘: 표시 base(8자리)에 인게임 <b>버프</b> 아이콘 PNG가 있으면 그것,
    /// 없으면 <see cref="Skill"/>과 같은 스킬 아이콘, 둘 다 없으면 null.
    /// <para>버프 아이콘은 8자리 표시 base로만 찾는다. 9자리 런타임 코드가 들어오면 스킬 아이콘 규칙으로만
    /// 떨어진다 — 같은 base로 접히는 디버프(투기 base의 당기기 등)가 버프 아이콘을 받지 않게 하려는 것이다.
    /// 그 구분은 런타임 코드 단위 매니페스트를 보는 쪽(상세 행)이 따로 해야 한다.</para>
    /// </summary>
    public static IconRef? BuffSlot(int displayBase, Func<int, bool> hasBuffIcon, Func<int, bool> hasSkillIcon)
    {
        if (displayBase is >= 11_000_000 and <= 19_999_999 && hasBuffIcon(displayBase))
        {
            return new IconRef(IconSet.Buff, displayBase);
        }

        return Skill(displayBase, hasSkillIcon) is int skill ? new IconRef(IconSet.Skill, skill) : null;
    }

    /// <summary>
    /// 정확한 코드에 PNG가 없을 때 차례로 볼 코드들.
    /// <list type="bullet">
    /// <item>스킬 코드(8자리 직업 대역): 바닥 base(<c>/10000*10000</c>).</item>
    /// <item>버프 코드(9자리 직업 대역): 표시 base(<see cref="DataManager.BuffDisplayBase"/>) → 그 바닥 base.
    /// 표시 base를 먼저 봐야 이름이 다른 효과를 따로 뗀 코드(대지의 축복)가 오버레이와 같은 아이콘을 쓴다.
    /// 바닥 단계는 그렇게 뗀 표시 코드에 전용 PNG가 없을 때(심판의 권능·보호의 방패·회피 자세 — 같은 base의
    /// 빈 번호) base 아이콘으로 떨어지게 한다. 그 밖의 코드는 표시 base가 이미 바닥이라 이 단계가 아무 일도
    /// 하지 않는다.</item>
    /// </list>
    /// </summary>
    public static IEnumerable<int> Fallbacks(int code)
    {
        switch (code)
        {
            case >= 11_000_000 and <= 19_999_999:
            {
                int floor = code / 10_000 * 10_000;
                if (floor != code)
                {
                    yield return floor;
                }

                break;
            }

            case >= 110_000_000 and <= 199_999_999:
            {
                int display = DataManager.BuffDisplayBase(code);
                yield return display;
                int floor = display / 10_000 * 10_000;
                if (floor != display)
                {
                    yield return floor;
                }

                break;
            }
        }
    }
}

/// <summary>아이콘 PNG 묶음: 스킬 아이콘(<c>Assets/SkillIcons</c>) 또는 인게임 버프 아이콘(<c>Assets/BuffIcons</c>).</summary>
public enum IconSet
{
    Skill,
    Buff,
}

/// <summary>어느 묶음의 어느 PNG(파일명 = 코드)를 그릴지.</summary>
public readonly record struct IconRef(IconSet Set, int Code);

using System.Globalization;

namespace WaffleMeter.App.Core;

/// <summary>
/// 버프 오버레이의 출하 기본값을 설정에 들이는 곳. 첫 실행에는 카탈로그의 기본 Off(<c>defaultOff</c>)를 숨김에
/// 넣고, 기존 사용자에게는 버전마다 한 번씩 그 뒤로 바뀐 기본값을 옮긴다.
/// <para><b>왜 버전 키가 필요한가(2026-11-11).</b> 호법성 '질주의 진언'은 켜 두는 동안 1초마다 다시 오는 on/off
/// 오라라 기본 Off였고, 첫 실행 기본값이 사용자의 숨김 목록에 그 base를 넣어 두었다. 클래스 패치로 같은 base가
/// 남은 시간이 있는 자기 버프 '폭풍의 주문'이 됐으니 기본 Off를 풀어야 하는데, 카탈로그의 <c>defaultOff</c>만
/// 고치면 이미 숨김 목록에 들어간 기존 사용자는 계속 숨겨진 채로 남는다. 그렇다고 버전 없이 매번 빼면 사용자가
/// 일부러 다시 끈 선택을 기동할 때마다 되돌린다. 그래서 단계마다 한 번만 돈다.</para>
/// <para><b>프리셋.</b> <see cref="BuffPresetManager"/>는 기동 때 활성 슬롯만 live 설정으로 다시 고치고 비활성
/// 슬롯은 blob 그대로 둔다. 비활성 슬롯의 숨김도 여기서 같이 고쳐야 그 슬롯을 고르는 순간 다시 숨겨지지 않는다.
/// 그러니 이 클래스는 반드시 <see cref="BuffPresetManager"/>를 만들기 <b>전에</b> 불려야 한다.</para>
/// <para>음성(<c>buffUi.voice</c>)과 고정(<c>buffUi.pinned</c>)은 건드리지 않는다 — 기본값이 바뀐 것은
/// 오버레이 표시 하나뿐이다.</para>
/// </summary>
public static class BuffUiDefaultsMigration
{
    /// <summary>지금 출하본의 기본값 버전. 단계를 추가하면 함께 올린다.</summary>
    public const int CurrentVersion = 1;

    /// <summary>버전마다 숨김에서 빼는 base 코드.</summary>
    public static IReadOnlyList<(int Version, int[] Unhide)> Steps { get; } =
    [
        (1, [18160000]), // 2026-11-11: 질주의 진언(on/off 오라, 기본 Off) → 폭풍의 주문(남은 시간이 있는 자기 버프)
    ];

    /// <summary>
    /// 첫 실행이면 <paramref name="defaultOff"/>를 숨김에 더하고 현재 버전을 기록한다(첫 실행은 이미 현재
    /// 기본값을 받았으므로 단계는 돌지 않는다). 기존 사용자면 저장된 버전 뒤의 단계를 live 숨김과 모든 프리셋
    /// 슬롯의 숨김에 적용하고 현재 버전을 기록한다. 이미 현재 버전이면 아무것도 쓰지 않는다.
    /// </summary>
    public static void Apply(MeterSettings settings, IEnumerable<int> defaultOff)
    {
        if (!settings.BuffUiDefaultsApplied)
        {
            HashSet<int> hidden = MeterSettings.ParseCodeSet(settings.BuffUiHidden);
            hidden.UnionWith(defaultOff);
            settings.BuffUiHidden = string.Join(",", hidden);
            settings.BuffUiDefaultsApplied = true;
            settings.BuffUiDefaultsVersion = CurrentVersion;
            return;
        }

        if (settings.BuffUiDefaultsVersion >= CurrentVersion)
        {
            return;
        }

        int from = settings.BuffUiDefaultsVersion;
        var unhide = Steps.Where(s => s.Version > from).SelectMany(s => s.Unhide).ToHashSet();
        settings.BuffUiHidden = RemoveCodes(settings.BuffUiHidden, unhide);

        // 없거나 깨진 blob 은 손대지 않는다 — BuffPresetManager 가 고친 live 설정으로 3슬롯을 새로 채운다.
        if (BuffPresetCodec.Decode(settings.BuffUiPresets) is { Slots: not null } set)
        {
            List<BuffPreset> slots = set.Slots.Select(p => p is { } slot ? slot with { Hidden = RemoveCodes(slot.Hidden ?? "", unhide) } : p).ToList();
            if (!slots.SequenceEqual(set.Slots))
            {
                settings.BuffUiPresets = BuffPresetCodec.Encode(set with { Slots = slots });
            }
        }

        settings.BuffUiDefaultsVersion = CurrentVersion;
    }

    /// <summary>
    /// <paramref name="csv"/>에서 <paramref name="codes"/>를 토큰 단위로 뺀다. 남은 토큰의 순서와 모양,
    /// 숫자가 아닌 토큰은 그대로 둔다. 뺄 것이 없으면 <paramref name="csv"/> 자체를 돌려준다.
    /// </summary>
    public static string RemoveCodes(string csv, IReadOnlySet<int> codes)
    {
        string[] parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!parts.Any(p => IsCode(p, out int c) && codes.Contains(c)))
        {
            return csv;
        }

        return string.Join(",", parts.Where(p => !(IsCode(p, out int c) && codes.Contains(c))));
    }

    private static bool IsCode(string part, out int code) =>
        int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
}

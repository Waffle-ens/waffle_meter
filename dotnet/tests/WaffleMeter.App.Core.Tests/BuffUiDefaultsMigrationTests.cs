using WaffleMeter.App.Core;
using WaffleMeter.Services;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for <see cref="BuffUiDefaultsMigration"/>: 2026-11-11 클래스 패치로 호법성 '질주의 진언'(on/off 오라,
/// 기본 Off)이 남은 시간이 있는 자기 버프 '폭풍의 주문'(18160000)이 됐다. 기존 사용자의 숨김 목록과 프리셋
/// 슬롯에 들어가 있던 18160000을 <b>한 번만</b> 빼고, 신규 사용자에게는 새 기본값만 준다.
/// </summary>
public sealed class BuffUiDefaultsMigrationTests : IDisposable
{
    private const int Storm = 18160000;     // 폭풍의 주문(구 질주의 진언)
    private const int Unyielding = 18190000; // 불패의 진언 — 여전히 기본 Off
    private static readonly int[] ShippedDefaultOff = [17410000, Unyielding];

    private readonly string _temp;

    public BuffUiDefaultsMigrationTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "wm_buffmig_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private MeterSettings OpenSettings() => new(new PropertyHandler(_temp));

    /// <summary>v3.4.0까지의 기존 사용자: 첫 실행 기본값은 받았고(옛 defaultOff에 18160000) 버전 키는 없다.</summary>
    private MeterSettings ExistingUser(string hidden)
    {
        MeterSettings s = OpenSettings();
        s.BuffUiHidden = hidden;
        s.BuffUiDefaultsApplied = true;
        return s;
    }

    private static string Slots(int active, params BuffPreset[] slots) =>
        BuffPresetCodec.Encode(new BuffPresetSet { Active = active, Slots = slots.ToList() });

    [Fact]
    public void First_run_hides_the_shipped_default_off_and_records_the_current_version()
    {
        MeterSettings s = OpenSettings();

        BuffUiDefaultsMigration.Apply(s, ShippedDefaultOff);

        Assert.Equal(new HashSet<int>(ShippedDefaultOff), MeterSettings.ParseCodeSet(s.BuffUiHidden));
        Assert.True(s.BuffUiDefaultsApplied);
        Assert.Equal(BuffUiDefaultsMigration.CurrentVersion, s.BuffUiDefaultsVersion);
        Assert.Equal(BuffUiDefaultsMigration.CurrentVersion, OpenSettings().BuffUiDefaultsVersion); // persisted
    }

    [Fact]
    public void First_run_keeps_what_the_user_already_hid()
    {
        MeterSettings s = OpenSettings();
        s.BuffUiHidden = "11100000";

        BuffUiDefaultsMigration.Apply(s, ShippedDefaultOff);

        Assert.Equal(new HashSet<int> { 11100000, 17410000, Unyielding }, MeterSettings.ParseCodeSet(s.BuffUiHidden));
    }

    [Fact]
    public void An_existing_user_loses_only_the_storm_spell_from_hidden_in_order()
    {
        MeterSettings s = ExistingUser($"17410000,{Storm},11100000,{Unyielding}");

        BuffUiDefaultsMigration.Apply(s, ShippedDefaultOff);

        Assert.Equal($"17410000,11100000,{Unyielding}", s.BuffUiHidden);
        Assert.Equal(BuffUiDefaultsMigration.CurrentVersion, s.BuffUiDefaultsVersion);
    }

    [Fact]
    public void An_existing_user_does_not_get_new_default_off_codes_added()
    {
        // 기본 Off 를 다시 들이는 건 첫 실행뿐이다 — 기존 사용자가 직접 켠 불패의 진언을 되돌리지 않는다.
        MeterSettings s = ExistingUser($"{Storm}");

        BuffUiDefaultsMigration.Apply(s, ShippedDefaultOff);

        Assert.Equal("", s.BuffUiHidden);
    }

    [Fact]
    public void Every_preset_slot_loses_the_storm_spell_and_keeps_everything_else()
    {
        MeterSettings s = ExistingUser($"{Storm},17410000");
        s.BuffUiPresets = Slots(1,
            new BuffPreset { Name = "레이드", Hidden = $"{Storm},17410000", Voice = $"{Storm}", Pinned = $"{Storm},11100000", IconSize = 50 },
            new BuffPreset { Name = "솔로", Hidden = $"17410000,{Storm}", TtsOnEnd = true },
            new BuffPreset { Name = "프리셋 3", Hidden = "11100000" });

        BuffUiDefaultsMigration.Apply(s, ShippedDefaultOff);

        BuffPresetSet set = Assert.IsType<BuffPresetSet>(BuffPresetCodec.Decode(s.BuffUiPresets));
        Assert.Equal(1, set.Active);
        Assert.Equal(new[] { "17410000", "17410000", "11100000" }, set.Slots.Select(p => p.Hidden));
        Assert.Equal(new[] { "레이드", "솔로", "프리셋 3" }, set.Slots.Select(p => p.Name));
        // 음성·고정은 이 마이그레이션의 대상이 아니다.
        Assert.Equal($"{Storm}", set.Slots[0].Voice);
        Assert.Equal($"{Storm},11100000", set.Slots[0].Pinned);
        Assert.Equal(50, set.Slots[0].IconSize);
        Assert.True(set.Slots[1].TtsOnEnd);
    }

    [Fact]
    public void It_runs_once_so_a_later_choice_to_hide_the_storm_spell_sticks()
    {
        MeterSettings s = ExistingUser($"{Storm}");
        BuffUiDefaultsMigration.Apply(s, ShippedDefaultOff);
        Assert.Equal("", s.BuffUiHidden);

        s.BuffUiHidden = $"{Storm}"; // 사용자가 picker 에서 다시 끈다
        MeterSettings reopened = OpenSettings();
        BuffUiDefaultsMigration.Apply(reopened, ShippedDefaultOff);

        Assert.Equal($"{Storm}", reopened.BuffUiHidden);
    }

    [Fact]
    public void Live_voice_and_pinned_are_untouched()
    {
        MeterSettings s = ExistingUser($"{Storm}");
        s.BuffUiVoice = $"{Storm}";
        s.BuffUiPinned = $"{Storm}";

        BuffUiDefaultsMigration.Apply(s, ShippedDefaultOff);

        Assert.Equal($"{Storm}", s.BuffUiVoice);
        Assert.Equal($"{Storm}", s.BuffUiPinned);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!")]
    public void A_missing_or_corrupt_preset_blob_is_left_for_the_manager_to_reseed(string blob)
    {
        MeterSettings s = ExistingUser($"{Storm},17410000");
        s.BuffUiPresets = blob;

        BuffUiDefaultsMigration.Apply(s, ShippedDefaultOff);

        Assert.Equal(blob, s.BuffUiPresets);
        using var manager = new BuffPresetManager(s, _ => { }, _ => { });
        BuffPresetSet set = Assert.IsType<BuffPresetSet>(BuffPresetCodec.Decode(s.BuffUiPresets));
        Assert.All(set.Slots, p => Assert.DoesNotContain(Storm, MeterSettings.ParseCodeSet(p.Hidden)));
    }

    [Fact]
    public void Non_code_tokens_are_kept()
    {
        Assert.Equal("abc,17410000", BuffUiDefaultsMigration.RemoveCodes($"abc,{Storm},17410000", new HashSet<int> { Storm }));
        const string untouched = " 17410000 , 11100000";
        Assert.Same(untouched, BuffUiDefaultsMigration.RemoveCodes(untouched, new HashSet<int> { Storm }));
    }
}

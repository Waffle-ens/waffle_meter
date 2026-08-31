using System.Globalization;
using System.Text;
using System.Text.Json;
using WaffleMeter.Capture;
using WaffleMeter.Data;

namespace WaffleMeter.App.Core;

/// <summary>Combat rates the meter MEASURED, which the stat window does not show and the stat dictionary does
/// not carry. They are observed frequencies over a real fight, not character stats.</summary>
/// <param name="CritHitRate">치명타 적중률 %, over every hit.</param>
/// <param name="DirectionalHitRate">전방/후방 타격률 %, over flag-bearing hits only (the same denominator the
/// combat detail uses — non-directional hits would otherwise dilute it).</param>
/// <param name="PreferBack">Whether back attacks outnumbered front ones, which picks the calculator's
/// direction toggle.</param>
public readonly record struct MeasuredCombatRates(double CritHitRate, double DirectionalHitRate, bool PreferBack);

/// <summary>
/// Turns the captured stat sheet into the two things the 와터기 stat calculator can consume: a clipboard blob
/// and a pre-filled calculator URL.
///
/// <para><b>Only fields whose meaning lines up are filled.</b> The calculator asks for several numbers the
/// stat dictionary does not contain and the in-game stat window does not show either — 장비 해제 공격력, the
/// weapon tooltip's 최소/최대 공격력, 장비 돌파 레벨 합계, and the 최대 공격력 합계 that means "the additive
/// max-attack bonuses" rather than "your maximum attack". Two ids in the sheet (31/33) look like the latter
/// and are NOT the same quantity, so they are deliberately left out: a plausible-looking wrong number in a
/// calculator is worse than an empty field the user knows to fill.</para>
///
/// <para>The URL uses the site's existing query contract (<c>?c=1&amp;ae=…</c>), which accepts a PARTIAL set —
/// keys we omit fall back to the calculator's defaults. That means the deep link works today with no change
/// on the site; the clipboard format is the one that needs a parser there, and it carries everything
/// (including the raw id→value map) so the site can grow into fields the URL has no key for.</para>
/// </summary>
public static class StatSheetExport
{
    /// <summary>클립보드 접두사. 붙여넣기 받는 쪽이 "이건 우리 포맷"임을 한 눈에 가릴 수 있게 한다.</summary>
    public const string ClipboardPrefix = "WAFFLE_STATS_V1:";

    /// <summary>스키마 이름/버전. 필드가 늘어나면 버전을 올린다 — <b>웹이 먼저</b> 새 버전을 읽을 수 있게 된 뒤에.</summary>
    public const string SchemaName = "waffle-stat-profile-v1";

    public const int SchemaVersion = 1;

    /// <summary>계산기 딥링크의 기본 주소. 도메인 자체는 <c>StatsApiClient</c>가 단독 보관하므로 호출부가
    /// <c>StatsApiClient.CalculatorPageUrl</c>을 넘긴다 — 여기 값은 그 인자를 안 넘겼을 때의 안전한 기본값이다.</summary>
    public const string CalculatorUrl = "https://xn--ok0b896b9wh.kr/calculator";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// 클립보드에 넣을 문자열: <c>WAFFLE_STATS_V1:</c> + UTF-8 JSON의 Base64.
    /// <para>Base64로 감싸는 이유는 숨기려는 게 아니라(그 안은 평문 JSON이다) 줄바꿈·공백이 섞인 붙여넣기에서도
    /// 한 덩어리로 살아남게 하려는 것이다.</para>
    /// </summary>
    public static string BuildClipboard(
        PlayerStatSheet sheet,
        string? jobName,
        string? nickname,
        int server,
        long capturedAt,
        MeasuredCombatRates? measured = null)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", SchemaName);
            writer.WriteNumber("version", SchemaVersion);
            writer.WriteNumber("capturedAt", capturedAt);
            writer.WriteBoolean("fullSnapshot", sheet.FullSnapshotSeen);
            if (!string.IsNullOrWhiteSpace(jobName)) writer.WriteString("job", jobName);
            if (!string.IsNullOrWhiteSpace(nickname)) writer.WriteString("nickname", nickname);
            if (server > 0) writer.WriteNumber("server", server);

            // Named stats, already in the unit a human reads: percent-ish ids divided by 100, the rest as-is.
            writer.WriteStartObject("stats");
            foreach ((int id, int value) in sheet.Values.OrderBy(kv => kv.Key))
            {
                string? label = PlayerStatIds.Label(id);
                if (label == null) continue;
                if (PlayerStatIds.IsPercent(id)) writer.WriteNumber(label, value / 100.0);
                else writer.WriteNumber(label, value);
            }

            if (sheet.CooldownPercent() is { } cooldown) writer.WriteNumber("쿨타임 감소", cooldown);
            writer.WriteEndObject();

            // 스탯창에 뜨는 합계. 와이어에는 없고 미터가 항을 합친 값이라 별도 블록으로 둔다 — 받는 쪽이
            // "이건 패킷 원본이 아니라 계산된 값"임을 구분할 수 있어야 한다.
            writer.WriteStartObject("derived");
            if (sheet.AttackPower() is { } ap) writer.WriteNumber("공격력", Math.Round(ap));
            if (sheet.DefensePower() is { } dp) writer.WriteNumber("방어력", Math.Round(dp));
            if (sheet.AccuracyTotal() is { } acc) writer.WriteNumber("명중", Math.Round(acc));
            if (sheet.CriticalTotal() is { } crit) writer.WriteNumber("치명타", Math.Round(crit));
            writer.WriteEndObject();

            if (measured is { } m)
            {
                // 스탯창에 없는 값들 — 미터가 실제 전투에서 센 빈도다. 계산기의 '전투 환경' 칸이 이걸 원한다.
                writer.WriteStartObject("measured");
                writer.WriteNumber("치명타 적중률", Math.Round(m.CritHitRate, 2));
                writer.WriteNumber("전방후방 타격률", Math.Round(m.DirectionalHitRate, 2));
                writer.WriteString("방향", m.PreferBack ? "back" : "front");
                writer.WriteEndObject();
            }

            // 이름을 아직 못 붙인 id까지 전부. 라벨링은 인게임 스탯창과 대조해야 확정되는데, 그 대조를 하려면
            // 값이 남아 있어야 한다 — 모르는 id를 버리면 영영 못 붙인다.
            writer.WriteStartObject("raw");
            foreach ((int id, int value) in sheet.Values.OrderBy(kv => kv.Key))
            {
                writer.WriteNumber(id.ToString(Inv), value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return ClipboardPrefix + Convert.ToBase64String(buffer.ToArray());
    }

    /// <summary>
    /// 계산기를 열 URL. 사이트의 기존 쿼리 계약(<c>c=1</c> + 축약 키)을 그대로 쓰므로 <b>웹 수정 없이</b> 동작한다.
    /// 못 채우는 칸은 아예 안 싣는다 — 사이트가 빠진 키를 기본값으로 두기 때문이고, 0을 실으면 "관통 0"처럼
    /// 사용자가 고칠 줄 모르는 거짓말이 된다.
    /// </summary>
    public static string BuildCalculatorUrl(
        PlayerStatSheet sheet, MeasuredCombatRates? measured = null, string baseUrl = CalculatorUrl)
    {
        var query = new List<string> { "c=1" };

        void Flat(string key, int statId)
        {
            if (sheet.Raw(statId) is { } v) query.Add($"{key}={v.ToString(Inv)}");
        }

        void Pct(string key, int statId)
        {
            if (sheet.Percent(statId) is { } v) query.Add($"{key}={v.ToString("0.##", Inv)}");
        }

        // 착용 공격력 = 스탯창에 뜨는 그 숫자다. 와이어에는 합계가 없고 항만 오므로 미터가 합친다
        // (PlayerStatSheet.AttackPower — 실측 대조로 소수점까지 일치). 항 하나(id 317)만 넣으면 실제의 28%
        // 수준이 되고, 계산기의 한계효율은 유한차분이라 분모가 작을수록 공격력 계열 1포인트가 커 보여
        // 인게임에서는 결코 강타 1%를 못 넘는 공격력 옵션이 최상위로 뒤집힌다.
        if (sheet.AttackPower() is { } attackPower)
        {
            query.Add($"ae={Math.Round(attackPower).ToString(Inv)}");
        }

        // 무기 damage range. 계산기가 이걸로 공격력 구간(min/max)을 만든다. 이 둘의 평균이 위 합계에 그대로
        // 들어간다는 것이 실측으로 확인됐으므로 같은 물건이 맞다.
        Flat("wn", PlayerStatIds.MinimumAttack);
        Flat("wx", PlayerStatIds.MaximumAttack);
        Flat("pe", PlayerStatIds.Penetration);       // 관통
        Flat("de", PlayerStatIds.Destruction);       // 파괴
        Flat("mi", PlayerStatIds.Might);             // 위력
        Pct("am", PlayerStatIds.DamageAmplifyPercent);
        Pct("wa", PlayerStatIds.WeaponDamageAmplifyPercent);
        Pct("ca", PlayerStatIds.CriticalDamageAmplifyPercent);
        Pct("sm", PlayerStatIds.HardHitPercent);
        Pct("pf", PlayerStatIds.PerfectPercent);
        Pct("pv", PlayerStatIds.PveDamageAmplifyPercent);
        Pct("ba", PlayerStatIds.BossDamageAmplifyPercent);
        Pct("mh", PlayerStatIds.AdditionalHitAccuracyPercent);

        // 방향 증폭은 앞/뒤가 따로 실린다. 실제로 그 방향으로 때린 비율이 큰 쪽을 고른다 — 계산기가 한 방향만
        // 받기 때문이다. 측정값이 없으면 값이 있는 쪽, 둘 다 있으면 큰 쪽.
        bool preferBack = measured?.PreferBack
            ?? (sheet.Percent(PlayerStatIds.BackDamageAmplifyPercent) ?? 0)
               >= (sheet.Percent(PlayerStatIds.FrontDamageAmplifyPercent) ?? 0);
        int directionalId = preferBack
            ? PlayerStatIds.BackDamageAmplifyPercent
            : PlayerStatIds.FrontDamageAmplifyPercent;
        Pct("da", directionalId);
        query.Add(preferBack ? "di=b" : "di=f");

        if (measured is { } m)
        {
            query.Add($"cr={m.CritHitRate.ToString("0.##", Inv)}");
            query.Add($"dh={m.DirectionalHitRate.ToString("0.##", Inv)}");
        }

        query.Add("p=1"); // PvE

        return baseUrl + "?" + string.Join("&", query);
    }

    /// <summary>사용자에게 보여줄 "무엇이 아직 안 채워졌나" 목록. 빈 목록이면 딥링크가 계산기 입력을 다 채운다는 뜻.
    /// <para>계산기가 요구하지만 <b>어떤 경로로도</b> 자동으로 못 채우는 칸은 여기 고정으로 들어간다 — 사용자가
    /// "왜 안 채워졌지"를 묻기 전에 답해 두는 쪽이 낫다.</para></summary>
    public static IReadOnlyList<string> UnfilledFields(PlayerStatSheet sheet)
    {
        _ = sheet;
        return new List<string>
        {
            // 장비를 벗어야 알 수 있는 값이라 어떤 패킷에도 없다.
            "장비 해제 공격력",
            // 계산기의 '최대 공격력 합계'는 펫 이해도·타이틀·날개의 가산치이고, 우리가 읽는 '무기 최대
            // 공격력'과 다른 물건이다. 섞으면 공격력 구간이 두 배로 커진다.
            "최대 공격력 합계",
            "장비 돌파 레벨 합계",
        };
    }

    /// <summary>
    /// The captured sheet, grouped the way a person reads a stat window rather than in id order.
    ///
    /// <para>The last group is every id we have not named yet, shown as <c>#id</c>. They are NOT hidden: the
    /// id→name mapping cannot be read off the wire (the packet carries numbers only), so the only way to
    /// confirm or extend it is for someone to put this list next to the in-game stat window. An id whose value
    /// we never showed is an id nobody can ever name.</para>
    /// </summary>
    public static IReadOnlyList<StatSheetGroup> Groups(PlayerStatSheet sheet)
    {
        var used = new HashSet<int>();

        StatSheetGroup Group(string title, params int[] ids)
        {
            var rows = new List<StatSheetRow>();
            foreach (int id in ids)
            {
                used.Add(id);
                if (sheet.Raw(id) is not { } value) continue;
                rows.Add(new StatSheetRow(PlayerStatIds.Label(id) ?? "#" + id.ToString(Inv), Format(id, value)));
            }

            return new StatSheetGroup(title, rows);
        }

        // 인게임 스탯창에 뜨는 합계부터 보여준다. 와이어에는 이 숫자가 없고 항만 오므로 미터가 합친 것인데,
        // 사용자가 대조할 때 제일 먼저 찾는 값이 이 넷이다. 아래 그룹들이 그 항들이다.
        var derived = new StatSheetGroup("인게임 스탯창 값 (미터가 합산)", new List<StatSheetRow>());
        void Derived(string label, double? value)
        {
            if (value is { } v) derived.Rows.Add(new StatSheetRow(label, Math.Round(v).ToString("N0", Inv)));
        }

        Derived("공격력", sheet.AttackPower());
        Derived("방어력", sheet.DefensePower());
        Derived("명중", sheet.AccuracyTotal());
        Derived("치명타", sheet.CriticalTotal());

        var groups = new List<StatSheetGroup>
        {
            derived,
            Group("공격",
                PlayerStatIds.Attack, PlayerStatIds.AdditionalAttack, PlayerStatIds.MaximumAttack,
                PlayerStatIds.MinimumAttack, PlayerStatIds.CriticalAttackPower, PlayerStatIds.Penetration,
                PlayerStatIds.PveAttack, PlayerStatIds.BossAttack, PlayerStatIds.FrontAttack,
                PlayerStatIds.BackAttack, PlayerStatIds.SealstoneAdditionalDamage),
            Group("증폭 · 판정",
                PlayerStatIds.DamageAmplifyPercent, PlayerStatIds.WeaponDamageAmplifyPercent,
                PlayerStatIds.PveDamageAmplifyPercent, PlayerStatIds.BossDamageAmplifyPercent,
                PlayerStatIds.CriticalDamageAmplifyPercent, PlayerStatIds.FrontDamageAmplifyPercent,
                PlayerStatIds.BackDamageAmplifyPercent, PlayerStatIds.HardHitPercent,
                PlayerStatIds.PerfectPercent, PlayerStatIds.AdditionalHitAccuracyPercent,
                PlayerStatIds.AttackIncreasePercent, PlayerStatIds.CombatSpeedPercent),
            Group("명중 · 치명",
                PlayerStatIds.Accuracy, PlayerStatIds.WeaponAccuracy, PlayerStatIds.PveAccuracy,
                PlayerStatIds.Critical, PlayerStatIds.FrontCritical, PlayerStatIds.BackCritical,
                PlayerStatIds.AccuracyIncreasePercent, PlayerStatIds.CriticalIncreasePercent),
            Group("방어",
                PlayerStatIds.Defense, PlayerStatIds.ArmorDefense, PlayerStatIds.DefenseIncreasePercent),
            Group("주신 스탯",
                PlayerStatIds.Might, PlayerStatIds.Agility, PlayerStatIds.Knowledge, PlayerStatIds.Vitality,
                PlayerStatIds.Precision, PlayerStatIds.Will, PlayerStatIds.Justice, PlayerStatIds.Freedom,
                PlayerStatIds.Illusion, PlayerStatIds.Life, PlayerStatIds.Time, PlayerStatIds.Destruction,
                PlayerStatIds.Death, PlayerStatIds.Wisdom, PlayerStatIds.Destiny, PlayerStatIds.Space),
        };

        // 쿨타임 감소는 두 id의 합이라 위 그룹 함수로는 못 만든다.
        used.Add(PlayerStatIds.CooldownBasePercent);
        used.Add(PlayerStatIds.CooldownBonusPercent);
        if (sheet.CooldownPercent() is { } cooldown)
        {
            groups[1].Rows.Add(new StatSheetRow("쿨타임 감소", cooldown.ToString("0.##", Inv) + "%"));
        }

        var unknown = new List<StatSheetRow>();
        foreach ((int id, int value) in sheet.Values.OrderBy(kv => kv.Key))
        {
            if (used.Contains(id)) continue;
            unknown.Add(new StatSheetRow("#" + id.ToString(Inv), value.ToString("N0", Inv)));
        }

        if (unknown.Count > 0)
        {
            groups.Add(new StatSheetGroup("아직 이름을 못 붙인 항목", unknown));
        }

        return groups.Where(g => g.Rows.Count > 0).ToList();
    }

    private static string Format(int id, int value) => PlayerStatIds.IsPercent(id)
        ? (value / 100.0).ToString("0.##", Inv) + "%"
        : value.ToString("N0", Inv);
}

/// <summary>One titled block of the stat sheet, as the settings screen lays it out.</summary>
public sealed record StatSheetGroup(string Title, List<StatSheetRow> Rows);

/// <summary>One stat line: a name and the value already in the unit a person reads.</summary>
public sealed record StatSheetRow(string Label, string Value);

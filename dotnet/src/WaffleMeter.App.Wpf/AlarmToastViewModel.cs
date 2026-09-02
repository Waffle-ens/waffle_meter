using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using WaffleMeter.App.Core;
using WaffleMeter.Capture;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// View model for the alarm toast (슈고 페스타 and, later, user custom alarms). UI-thread only. The glyph is a
/// Segoe MDL2 Assets code point (a ringer/bell), built from a char code to avoid a literal PUA char.
/// </summary>
public sealed class AlarmToastViewModel : INotifyPropertyChanged
{
    private static readonly Brush Amber = Frozen(Color.FromRgb(0xFB, 0xBF, 0x24));
    private static readonly string GlyphBell = ((char)0xEA8F).ToString(); // Ringer

    private string _title = string.Empty;
    public string Title { get => _title; private set => Set(ref _title, value); }

    /// <summary>
    /// The alert read aloud, which is deliberately NOT derived from <see cref="Description"/>.
    ///
    /// <para><b>It has to be a closed set.</b> The shipped voice packs are pre-rendered clips looked up by a
    /// hash of this exact string, so anything that varies per occurrence makes the line unspeakable from the
    /// pack. <c>Description</c> carries a wall-clock time for field bosses — useful on screen, fatal here:
    /// it made every respawn alert a unique string (77 bosses × leads × 1440 minutes), which also meant the
    /// old runtime cache never once hit on them.</para>
    ///
    /// <para><b>No full stops.</b> Measured on the synthesiser, a period between the name and the time buys a
    /// 0.7–1.0 s dead pause that reads as the voice dragging. A comma gives ~0.4 s, which is the beat we
    /// actually want. The middle dot is dropped for the same reason — it is punctuation the reader stumbles on
    /// but the screen still shows it.</para>
    /// </summary>
    private string _spokenText = string.Empty;
    public string SpokenText { get => _spokenText; private set => Set(ref _spokenText, value); }

    private string _description = string.Empty;
    public string Description { get => _description; private set => Set(ref _description, value); }

    private string _iconGlyph = GlyphBell;
    public string IconGlyph { get => _iconGlyph; private set => Set(ref _iconGlyph, value); }

    private Brush _iconBrush = Amber;
    public Brush IconBrush { get => _iconBrush; private set => Set(ref _iconBrush, value); }

    /// <summary>Set the toast for a 슈고 페스타 cue. <paramref name="lead"/> 0 = 시작, else N분 전.</summary>
    public void SetShugo(int lead)
    {
        Title = "슈고 페스타";
        Description = lead <= 0 ? "지금 시작합니다!" : $"{lead}분 뒤 시작합니다.";
        SpokenText = lead <= 0 ? "슈고 페스타, 지금 시작합니다" : $"슈고 페스타, {lead}분 뒤 시작합니다";
        IconGlyph = GlyphBell;
        IconBrush = Amber;
    }

    /// <summary>Set the toast for a field-boss respawn reminder.</summary>
    public void SetFieldBoss(string bossName, int leadMinutes, DateTime respawn)
    {
        Title = bossName;
        Description = $"{leadMinutes}분 뒤 리젠 · {respawn:HH:mm}";
        // 시각 제외 — 위 SpokenText 주석 참고. 표시명과 읽는 이름이 다른 보스가 있다(SpokenName).
        SpokenText = $"{SpokenName.Of(bossName)}, {leadMinutes}분 뒤 리젠";
        IconGlyph = GlyphBell;
        IconBrush = Amber;
    }

    /// <summary>Set the toast for the 감시자 카이라 spawn cue.
    /// <para>2026-09-02 패치로 출현이 <b>100% 확정</b>이 됐으므로 확률 20% 시절의 "출현 가능"을 버렸다.
    /// 확정 스케줄이 되면서 다음 출현 정각을 계산할 수 있게 됐으니 <see cref="SetFieldBoss"/> 와 같은 모양으로
    /// 시각도 함께 보여 준다.</para>
    /// <para>발화는 "…분 뒤 출현합니다" — 완결 평서문이라야 한다. 옛 문구는 명사로 끝나는 조각이라 종결
    /// 하강이 없었고, 그게 팩 레퍼런스로 쓰였을 땐 억양이 통째로 오염됐다(<c>Assets/voice/_source/README.md</c>).
    /// "리젠"을 쓰지 않는 이유는 두 가지다: (1) 이 보스는 처치 후 재생성이 아니라 고정 시각 출현이고 —
    /// 서버가 리젠 시각을 0으로 보내는 유일한 보스라는 게 이 알림이 따로 존재하는 이유 그 자체다,
    /// (2) 같은 어비스 하층에 <b>집행자 카이라</b>(2600098)가 있어 "…카이라, 10분 뒤 리젠"이 어절 하나만
    /// 다른 두 발화로 겹친다.</para>
    /// <para>lead 0 분기는 없다 — <see cref="KairaAlarm.EnabledLeads"/> 가 10/5/1 만 넣는다. '출현 시점' 토글을
    /// 열게 되면 여기에 분기를 되살리고 그 문구를 새로 구워야 한다.</para></summary>
    public void SetKaira(int leadMinutes, DateTime spawn)
    {
        // 이름은 카탈로그에서 읽는다 — 리터럴로 박으면 몹 이름 교정이 들어와도 여기만 옛 이름을 말하고,
        // 팩 테스트는 자기가 적어 둔 같은 리터럴과 비교하느라 그린인 채로 지나간다. 다른 보스 알림
        // (SetFieldBoss)은 이미 FieldBossCatalog.Name 을 거쳐 오므로 카이라만 예외였다.
        string name = FieldBossCatalog.Name(FieldBossCatalog.ScheduledSpawnCode);
        Title = name;
        Description = $"{leadMinutes}분 뒤 출현 · {spawn:HH:mm}";
        // 시각 제외 — 위 SpokenText 주석 참고(구운 클립은 문구 해시 주소라 가변 시각을 담을 수 없다).
        SpokenText = $"{SpokenName.Of(name)}, {leadMinutes}분 뒤 출현합니다";
        IconGlyph = GlyphBell;
        IconBrush = Amber;
    }

    /// <summary>Set the toast for a user custom alarm.</summary>
    public void SetCustom(string title)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "알람" : title;
        Description = "지금입니다.";
        // 자유 입력 제목이라 미리 구울 수 없다 — 이 줄만 온라인 합성으로 넘어간다.
        SpokenText = $"{Title}, 지금입니다";
        IconGlyph = GlyphBell;
        IconBrush = Amber;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

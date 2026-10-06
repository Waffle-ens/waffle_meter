using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WaffleMeter.App.Core;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// 메모 오버레이와 설정 '메모' 탭이 <b>함께</b> 보는 뷰모델. 본문은 <see cref="MemoTextStore"/>(memo.txt)에
/// 있고, 나머지(표시·투명도·색·잠금)는 <see cref="MeterSettings"/> 를 그대로 노출한다 — 메모 머리줄의
/// 투명도 슬라이더와 설정 탭 슬라이더가 <b>같은 경로</b>(<c>Settings.MemoOpacity</c>)로 묶여야 두 화면이 한
/// 값을 본다(미터 투명도와 같은 규칙).
/// <para>본문 저장은 디바운스한다. <see cref="Text"/> 세터는 메모리만 바꾸고 0.7초 타이머를 다시 건다 —
/// 설정 탭 TextBox 는 글자마다 이 세터를 부르므로, 바로 쓰면 한 단어에 파일을 열 번 넘게 다시 쓴다.
/// 종료·업데이트 재시작 직전에는 <see cref="Flush"/> 로 남은 것을 내려 쓴다. UI 스레드 전용.</para>
/// </summary>
public sealed class MemoOverlayViewModel : INotifyPropertyChanged
{
    private static readonly Brush FallbackTextBrush = Freeze(new SolidColorBrush(Colors.White));

    private readonly MemoTextStore _store;
    private readonly DispatcherTimer _saveTimer;

    public MemoOverlayViewModel(MeterSettings settings, MemoTextStore store)
    {
        Settings = settings;
        _store = store;
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(MemoTextStore.SaveDebounceMs),
        };
        _saveTimer.Tick += (_, _) => Flush();
        _textBrush = BuildTextBrush(settings.MemoTextColor);
        settings.PropertyChanged += OnSettingsChanged;
    }

    /// <summary>설정 원본. 머리줄 슬라이더·글꼴이 <c>Settings.*</c> 로 직접 묶인다.</summary>
    public MeterSettings Settings { get; }

    /// <summary>메모 본문. 오버레이(읽기·편집 커밋)와 설정 탭 TextBox 가 같은 값을 읽고 쓴다.</summary>
    public string Text
    {
        get => _store.Text;
        set
        {
            if (!_store.Update(value))
            {
                return;
            }

            // 마지막 입력에서 0.7초 — 타이머를 매번 다시 건다.
            _saveTimer.Stop();
            _saveTimer.Start();
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlaceholderVisibility));
        }
    }

    /// <summary>미뤄 둔 저장을 지금 한다(종료, 업데이트 재시작, 디바운스 만료).</summary>
    public void Flush()
    {
        _saveTimer.Stop();
        _store.Flush();
    }

    private Brush _textBrush;

    /// <summary>글씨 색. <see cref="ColorString.TryParse"/> 로 읽는다 — 피커가 내는 <c>rgba()</c> 를 WPF
    /// ColorConverter 는 못 읽어 조용히 흰색으로 떨어진다(버프·쿨타임 쪽에 남아 있는 잠복 결함).</summary>
    public Brush TextBrush
    {
        get => _textBrush;
        private set
        {
            _textBrush = value;
            OnPropertyChanged();
        }
    }

    /// <summary>투명도 최하 — 머리줄(배경·테두리·제목·닫기)을 지우고 슬라이더와 글자만 남긴다.</summary>
    public bool ChromeHidden => MemoOverlayPolicy.IsChromeHidden(Settings.MemoOpacity);

    /// <summary>Collapsed 가 아니라 Hidden 이다 — 슬라이더를 0 까지 끄는 도중에 제목이 접히면 레이아웃이
    /// 다시 흘러 슬라이더가 커서 밑에서 옆으로 튄다. 자리를 지킨 채 안 보이게만 한다.</summary>
    public Visibility ChromeVisibility => ChromeHidden ? Visibility.Hidden : Visibility.Visible;

    /// <summary>투명도 최하에선 넘친 본문의 스크롤바도 숨긴다(슬라이더와 글자만 남긴다는 결정). Hidden 은
    /// Disabled 와 달리 휠 스크롤은 그대로 받는다.</summary>
    public ScrollBarVisibility ReadScrollBarVisibility => ChromeHidden ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;

    private bool _ctrlHover;

    /// <summary>Ctrl 을 누른 채 커서가 메모 위에 있는가(창의 40ms 폴이 세운다). 투명도 최하에서 잠금 버튼을
    /// 꺼내는 조건이다.</summary>
    public bool CtrlHover
    {
        get => _ctrlHover;
        set
        {
            if (_ctrlHover == value)
            {
                return;
            }

            _ctrlHover = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LockButtonVisibility));
        }
    }

    /// <summary>잠금 버튼. 역시 Hidden — 사각형이 레이아웃에 남아 있어야 폴이 "커서가 버튼 위인가"를 잴 수
    /// 있다(숨은 버튼의 자리로 커서를 가져가면 그때 나타난다).</summary>
    public Visibility LockButtonVisibility =>
        MemoOverlayPolicy.ShowLockButton(ChromeHidden, _ctrlHover) ? Visibility.Visible : Visibility.Hidden;

    /// <summary>알파 1/255 바닥. 잠금이 풀린 동안만 깐다 — 레이어드 창은 알파 0 픽셀에서 클릭이 통과하므로,
    /// 투명도 0 의 메모는 바닥이 없으면 글자 획 위에서만 눌리고 드래그·가장자리 리사이즈·더블클릭이 다 죽는다.
    /// 잠긴 동안은 어차피 창 전체가 통과라 깔 이유가 없다.</summary>
    public Visibility HitFloorVisibility => Settings.MemoLocked ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Segoe MDL2: 잠김 E72E / 풀림 E785.</summary>
    public string LockGlyph => Settings.MemoLocked ? "" : "";

    public string LockToolTip => Settings.MemoLocked
        ? "잠김 — 클릭이 게임으로 통과합니다.\nCtrl 을 누른 채 이 버튼을 클릭하면 잠금이 풀립니다."
        : "잠그기 — 메모가 클릭을 게임으로 통과시킵니다.\n잠근 뒤에는 Ctrl 을 누른 채 이 버튼을 클릭해 풀 수 있습니다.";

    private bool _isEditing;

    /// <summary>오버레이 안 편집 모드(본문 더블클릭). 읽기용 TextBlock 과 편집용 TextBox 를 바꿔 끼운다.</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value)
            {
                return;
            }

            _isEditing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReadVisibility));
            OnPropertyChanged(nameof(EditVisibility));
            OnPropertyChanged(nameof(PlaceholderVisibility));
        }
    }

    public Visibility ReadVisibility => _isEditing ? Visibility.Collapsed : Visibility.Visible;

    public Visibility EditVisibility => _isEditing ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>빈 메모의 안내문. 잠긴 동안에는 띄우지 않는다 — 더블클릭이 안 되는 상태에서 "더블클릭하라"고
    /// 말하면 거짓말이고, 비어 있는 잠긴 메모는 아무것도 안 그리는 게 맞다.</summary>
    public Visibility PlaceholderVisibility =>
        _store.Text.Length == 0 && !_isEditing && !Settings.MemoLocked ? Visibility.Visible : Visibility.Collapsed;

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 빈 이름 = 설정 코드 가져오기(MeterSettings.Reload). 전부 다시 읽는다.
        bool all = string.IsNullOrEmpty(e.PropertyName);
        if (all || e.PropertyName == nameof(MeterSettings.MemoTextColor))
        {
            TextBrush = BuildTextBrush(Settings.MemoTextColor);
        }

        if (all || e.PropertyName == nameof(MeterSettings.MemoOpacity))
        {
            OnPropertyChanged(nameof(ChromeHidden));
            OnPropertyChanged(nameof(ChromeVisibility));
            OnPropertyChanged(nameof(ReadScrollBarVisibility));
            OnPropertyChanged(nameof(LockButtonVisibility));
        }

        if (all || e.PropertyName == nameof(MeterSettings.MemoLocked))
        {
            OnPropertyChanged(nameof(HitFloorVisibility));
            OnPropertyChanged(nameof(LockGlyph));
            OnPropertyChanged(nameof(LockToolTip));
            OnPropertyChanged(nameof(PlaceholderVisibility));
        }
    }

    private static Brush BuildTextBrush(string? raw) =>
        ColorString.TryParse(raw, out ColorRgba c)
            ? Freeze(new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B)))
            : FallbackTextBrush;

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

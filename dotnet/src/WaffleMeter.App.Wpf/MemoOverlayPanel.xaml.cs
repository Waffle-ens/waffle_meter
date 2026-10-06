using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WaffleMeter.App.Core;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// 게임 화면 위의 메모 한 장. <see cref="OverlayPanelWindow"/> 의 창 규약(NOACTIVATE·TOOLWINDOW·최상위 재선점·
/// Present/Fade/Park)을 그대로 쓰고, 그 위에 두 가지를 얹는다.
/// <list type="bullet">
/// <item><b>잠금</b> — 창 전체 클릭 통과(WS_EX_TRANSPARENT). 잠긴 창에는 클릭이 아예 오지 않으므로, 풀려면
/// 클릭 <b>전에</b> 통과를 걷어야 한다. 잠긴 동안만 40ms 폴로 Ctrl 과 커서를 보고, Ctrl 을 쥔 채 커서가
/// 잠금 버튼 위일 때만 그 순간 통과를 걷는다(<see cref="OverlayPanelWindow.SetHitTestPeek"/>). 메모 다른 곳의
/// Ctrl+클릭은 그대로 게임으로 간다.</item>
/// <item><b>오버레이 안 편집</b> — 잠금이 풀린 상태에서 본문을 더블클릭하면 그동안만 NOACTIVATE 를 내리고
/// 활성화해 키보드·IME 를 받는다. Esc 나 바깥 클릭(비활성화)에서 저장하고 원래대로 돌린다.</item>
/// </list>
/// <para>표시 여부는 App 이 정한다(<see cref="MemoOverlayPolicy.IsVisible"/>). 이 창은 잠금을 반영하고 편집
/// 상태를 알려 줄 뿐이다.</para>
/// </summary>
public partial class MemoOverlayPanel : OverlayPanelWindow
{
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int VkControl = 0x11;
    private const int SmSwapButton = 23;

    /// <summary>엿보기 폴 주기. 사람이 Ctrl 을 누르고 버튼 위로 커서를 옮겨 클릭하는 데 걸리는 시간보다 한참
    /// 짧으면 된다. 한 틱은 GetAsyncKeyState·GetCursorPos 두 번뿐이라 비용은 무시할 만하다.</summary>
    private static readonly TimeSpan PeekInterval = TimeSpan.FromMilliseconds(40);

    private readonly DispatcherTimer _peekTimer;
    private MemoOverlayViewModel? _vm;
    private bool _locked;
    private bool _peeking;
    private bool _presented;
    private bool _editing;
    private bool _hotkeysSuspended;
    private IntPtr _foregroundBeforeEdit;

    public MemoOverlayPanel()
    {
        InitializeComponent();
        _peekTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = PeekInterval };
        _peekTimer.Tick += OnPeekTick;
        DataContextChanged += OnDataContextChanged;
        // 바깥을 클릭해 포커스가 넘어갔다 = 편집 끝. 포그라운드는 이미 사용자가 고른 곳이니 되돌리지 않는다.
        Deactivated += (_, _) => CommitEdit(restoreForeground: false);
    }

    /// <summary>오버레이 안에서 편집 중인가. 편집하는 동안은 표시 규칙이 메모를 치우지 않는다.</summary>
    public bool IsEditing => _editing;

    /// <summary>잠금(클릭 통과) 상태를 반영한다. App 이 설정 변경과 주기 틱마다 부른다 — 멱등.</summary>
    public void SetLocked(bool locked)
    {
        if (_locked != locked)
        {
            _locked = locked;
            if (locked)
            {
                // 잠그는 순간 편집이 열려 있었다면(설정 탭 토글 등) 그대로 저장하고 닫는다 — 잠긴 창은 활성화도
                // 입력도 받지 않아야 한다.
                CommitEdit(restoreForeground: false);
            }
        }

        SetClickThrough(locked);
        if (!locked)
        {
            StopPeek();
        }

        UpdatePollTimer();
    }

    protected override void OnPresented()
    {
        _presented = true;
        UpdatePollTimer();
    }

    protected override void OnParked()
    {
        _presented = false;
        UpdatePollTimer();
        CommitEdit(restoreForeground: false);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelChanged;
        }

        _vm = DataContext as MemoOverlayViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelChanged;
        }

        UpdatePollTimer();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 투명도가 최하로 내려가거나 올라오면 폴을 켜고 끈다(최하에서는 Ctrl+호버로 잠금 버튼을 꺼내야 한다).
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(MemoOverlayViewModel.ChromeHidden))
        {
            UpdatePollTimer();
        }
    }

    // ---- 잠금 · 엿보기 ----

    private void UpdatePollTimer()
    {
        bool run = _presented && MemoOverlayPolicy.NeedsPeekPoll(_locked, _vm?.ChromeHidden ?? false);
        if (run)
        {
            if (!_peekTimer.IsEnabled)
            {
                _peekTimer.Start();
            }

            return;
        }

        _peekTimer.Stop();
        StopPeek();
        if (_vm is not null)
        {
            _vm.CtrlHover = false;
        }
    }

    private void StopPeek()
    {
        if (_peeking)
        {
            _peeking = false;
            SetHitTestPeek(false);
        }
    }

    private void OnPeekTick(object? sender, EventArgs e)
    {
        if (!GetCursorPos(out NativePoint cursor))
        {
            return;
        }

        bool ctrl = IsDown(VkControl);
        bool overWindow = IsOverWindow(cursor);
        if (_vm is not null)
        {
            _vm.CtrlHover = ctrl && overWindow;
        }

        // 커서가 메모 밖이면(게임 중 대부분의 틱) 버튼 사각형도, 마우스 버튼도 물을 필요가 없다.
        bool peek = MemoOverlayPolicy.ShouldPeek(
            _locked,
            ctrl,
            overWindow && IsOver(LockButton, cursor),
            _peeking,
            _peeking && IsDown(PrimaryButtonVk()));
        if (peek != _peeking)
        {
            _peeking = peek;
            SetHitTestPeek(peek);
        }
    }

    private void OnLockButton(object sender, RoutedEventArgs e)
    {
        if (_vm is not { } vm)
        {
            return;
        }

        if (vm.Settings.MemoLocked)
        {
            // 잠긴 창은 Ctrl 을 쥔 동안만 엿보기로 눌린다. 그래도 누르는 순간 Ctrl 을 다시 본다 — 엿보던 중
            // Ctrl 을 놓고 누른 평클릭이 잠금을 풀면 안 된다. Keyboard.Modifiers 는 쓰지 않는다: NOACTIVATE
            // 창의 스레드는 키보드 메시지를 안 받아 그 값이 갱신되지 않는다.
            if (!IsDown(VkControl))
            {
                return;
            }

            vm.Settings.MemoLocked = false;
        }
        else
        {
            vm.Settings.MemoLocked = true;
        }

        e.Handled = true;
    }

    // ---- 오버레이 안 편집 ----

    private void OnBodyMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_editing || _vm is null || _vm.Settings.MemoLocked)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            OnDragSurfaceUp(sender, e); // 첫 클릭이 걸어 둔 표면 드래그 무장을 푼다
            BeginEdit();
            e.Handled = true;
            return;
        }

        OnDragSurfaceDown(sender, e);
    }

    private void BeginEdit()
    {
        if (_editing || _locked || _vm is null)
        {
            return;
        }

        _editing = true;
        _foregroundBeforeEdit = GetForegroundWindow();
        EditBox.Text = _vm.Text;
        _vm.IsEditing = true;

        // 편집하는 동안만 이 창의 NOACTIVATE 를 내린다. 더블클릭이 우리 스레드에 온 마지막 입력이라
        // SetForegroundWindow 가 허용된다(포그라운드 잠금 규칙).
        SetActivatable(true);
        if (!Activate())
        {
            // 활성화를 못 받으면 키보드도 못 받는다. 그대로 두면 바깥 클릭으로도 끝나지 않는(활성화된 적이 없으니
            // Deactivated 가 안 온다) 편집 상자가 남으므로 바로 접는다 — 본문은 그대로다.
            CommitEdit(restoreForeground: false);
            return;
        }

        // 편집 상자는 방금 Visible 이 됐다 — 레이아웃이 한 번 돈 뒤에 포커스를 줘야 확실히 잡힌다.
        Dispatcher.InvokeAsync(() =>
        {
            if (!_editing)
            {
                return;
            }

            EditBox.Focus();
            Keyboard.Focus(EditBox);
            EditBox.CaretIndex = EditBox.Text.Length;
            EditBox.ScrollToEnd();
        }, DispatcherPriority.Input);
    }

    /// <summary>편집을 끝내고 저장한다. <paramref name="restoreForeground"/> 면 편집 전 포그라운드(대개 게임)로
    /// 돌려준다 — Esc 경로. 바깥 클릭(비활성화)이면 포그라운드는 이미 사용자가 고른 곳이라 건드리지 않는다.</summary>
    private void CommitEdit(bool restoreForeground)
    {
        if (!_editing)
        {
            return;
        }

        _editing = false;
        string edited = EditBox.Text;

        // 포커스를 먼저 놓는다(상자를 접기 전에) — 그래야 LostKeyboardFocus 가 확실히 와서 단축키가 돌아온다.
        if (EditBox.IsKeyboardFocusWithin)
        {
            Keyboard.ClearFocus();
        }

        ResumeHotkeys();
        if (_vm is { } vm)
        {
            vm.Text = edited;
            vm.IsEditing = false;
        }

        SetActivatable(false);

        IntPtr previous = _foregroundBeforeEdit;
        _foregroundBeforeEdit = IntPtr.Zero;
        if (restoreForeground && previous != IntPtr.Zero && IsWindow(previous))
        {
            SetForegroundWindow(previous);
        }
    }

    private void OnEditKeyDown(object sender, KeyEventArgs e)
    {
        // Esc = 저장하고 닫기(취소가 아니다). IME 가 조합 중이면 Esc 는 IME 몫이라 여기 Key.Escape 로 오지 않는다.
        if (e.Key == Key.Escape)
        {
            CommitEdit(restoreForeground: true);
            e.Handled = true;
        }
    }

    // 입력하는 동안 전역 단축키를 내린다 — RegisterHotKey 로 잡힌 조합(단일 키 단축키도 허용된다)은 OS 가
    // 가로채 WM_HOTKEY 로 보내므로 글자가 상자에 닿지 않고 미터가 숨거나 패널이 열린다. 단축키 입력 상자
    // (HotkeyCaptureBox)와 같은 장치를 쓴다.
    private void OnEditGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_hotkeysSuspended)
        {
            _hotkeysSuspended = true;
            HotkeyCaptureBox.SuspendGlobalHotkeys?.Invoke(true);
        }
    }

    private void OnEditLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ResumeHotkeys();

    private void ResumeHotkeys()
    {
        if (_hotkeysSuspended)
        {
            _hotkeysSuspended = false;
            HotkeyCaptureBox.SuspendGlobalHotkeys?.Invoke(false);
        }
    }

    // ---- 화면 좌표 판정 (PerMonitorV2: GetCursorPos 와 PointToScreen 이 둘 다 물리 픽셀) ----

    private bool IsOverWindow(NativePoint p)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        return hwnd != IntPtr.Zero
            && GetWindowRect(hwnd, out NativeRect r)
            && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
    }

    /// <summary>커서가 요소의 화면 사각형 안인가. 숨김(Hidden) 상태의 요소도 레이아웃 자리는 남아 있어 잴 수
    /// 있다 — 투명도 최하에서 잠금 버튼이 숨어 있는 동안에도 그 자리를 알아야 한다.</summary>
    private static bool IsOver(FrameworkElement element, NativePoint p)
    {
        try
        {
            if (PresentationSource.FromVisual(element) is null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            {
                return false;
            }

            Point a = element.PointToScreen(new Point(0, 0));
            Point b = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
            return p.X >= Math.Min(a.X, b.X) && p.X < Math.Max(a.X, b.X)
                && p.Y >= Math.Min(a.Y, b.Y) && p.Y < Math.Max(a.Y, b.Y);
        }
        catch (InvalidOperationException)
        {
            return false; // 창이 막 내려가는 중(프레젠테이션 소스 분리)
        }
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>GetAsyncKeyState 는 <b>물리</b> 버튼을 본다. 좌우를 바꿔 쓰는 사용자에게 "왼쪽 클릭"은 물리 오른쪽이다.</summary>
    private static int PrimaryButtonVk() => GetSystemMetrics(SmSwapButton) != 0 ? VkRButton : VkLButton;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);
}

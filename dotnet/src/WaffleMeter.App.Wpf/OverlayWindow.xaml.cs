using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace WaffleMeter.App.Wpf;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExAppWindow = 0x00040000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExTopmost = 0x00000008;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;

    private const uint GwHwndPrev = 3; // GetWindow: the window ABOVE us in z-order (within our band)
    private const uint GwOwner = 4;    // GetWindow: this window's owner (none by default; see ForceTopmost)

    private static readonly IntPtr HwndTopMost = new(-1);
    private static readonly IntPtr HwndTop = new(0);
    private static readonly IntPtr HwndNoTopMost = new(-2);
    private static readonly IntPtr HwndBottom = new(1);

    private const int WmActivate = 0x0006;
    private const int WmWindowPosChanged = 0x0047;

    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;

    private IntPtr _handle;
    private bool _clickThrough;
    private bool _taskbarMode;
    private bool _parked; // auto-hidden (Opacity 0); while parked the taskbar/Alt+Tab ex-style is dropped
    private bool? _presentedTopMost; // last applied present state; null = parked (forces re-present)
    public bool ClickThrough => _clickThrough;
    public bool TaskbarMode => _taskbarMode;

    /// <summary>Diagnostic-only: whether the overlay is currently auto-hide parked (Opacity 0). Read by
    /// <see cref="OverlayController"/>'s foreground-state trace. See overlay-autohide-unpark-on-return-rootcause.</summary>
    public bool DiagParked => _parked;

    /// <summary>Diagnostic-only: whether the HWND actually carries WS_EX_TOPMOST right now. This is the real
    /// symptom — a "presented" overlay (parked=false) with topmost=false is buried behind a fullscreen game.</summary>
    public bool DiagTopmost => _handle != IntPtr.Zero && (GetWindowLong(_handle, GwlExStyle) & WsExTopmost) != 0;

    /// <summary>Raised after a drag completes with the new Left/Top (App persists it).</summary>
    public event Action<double, double>? PositionChanged;

    /// <summary>Raised from the right-click menu (App opens the settings window / exits).</summary>
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    /// <summary>Header buttons (App handles): reset the meter / toggle taskbar (alt-tab) mode / open
    /// the battle-history panel.</summary>
    public event Action? ResetRequested;
    public event Action? TaskbarToggleRequested;
    public event Action? HistoryRequested;
    public event Action? ThemeRequested;
    public event Action? JoinRequested;

    /// <summary>Header update badge clicked (App shows the restart toast on demand).</summary>
    public event Action? UpdateRequested;

    public OverlayWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        // This window keeps ShowInTaskbar at its WPF default (true) — it must NOT be set to false in XAML.
        // WPF implements ShowInTaskbar=false by giving the window a hidden, NON-topmost OWNER window, and an
        // owned window cannot stay topmost above a borderless-fullscreen game — it gets pinned behind it
        // (the "overlay invisible after returning to the game" bug). The taskbar/Alt+Tab button is hidden
        // via WS_EX_TOOLWINDOW in SyncInputStyle instead.
        SyncInputStyle();
        // Re-assert the ex-style on focus/z-order changes so WPF can't strip TOOLWINDOW|NOACTIVATE
        // (the taskbar-flicker / focus-steal fix).
        HwndSource.FromHwnd(_handle)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is WmActivate or WmWindowPosChanged)
        {
            SyncInputStyle();
        }

        return IntPtr.Zero;
    }

    /// <summary>The single place the ex-style is asserted (TOOLWINDOW|LAYERED|NOACTIVATE &amp; ~APPWINDOW,
    /// toggling TRANSPARENT for click-through). No-op when unchanged.</summary>
    public void SyncInputStyle()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        int current = GetWindowLong(_handle, GwlExStyle);
        // Taskbar/Alt+Tab listing (APPWINDOW, activatable, no TOOLWINDOW/NOACTIVATE) applies ONLY while the
        // overlay is presented; a parked (auto-hidden) overlay falls back to the game-overlay style
        // (TOOLWINDOW|NOACTIVATE) so it leaves no blank taskbar/Alt+Tab entry. This keeps taskbar mode
        // (Option 2) orthogonal to auto-hide (Option 1): the listing follows Option 2, but only when the
        // overlay is actually on screen.
        bool taskbar = _taskbarMode && !_parked;
        int baseStyle = taskbar
            ? (current | WsExLayered | WsExAppWindow) & ~WsExToolWindow & ~WsExNoActivate
            : (current | WsExToolWindow | WsExLayered | WsExNoActivate) & ~WsExAppWindow;
        int next = _clickThrough ? baseStyle | WsExTransparent : baseStyle & ~WsExTransparent;
        if (next == current)
        {
            return;
        }

        SetWindowLong(_handle, GwlExStyle, next);
        SetWindowPos(_handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    public void SetClickThrough(bool enable)
    {
        _clickThrough = enable;
        SyncInputStyle();
        NotifyClickThrough();
    }

    /// <summary>Push the current click-through state to the view model so the header lock badge tracks it.</summary>
    private void NotifyClickThrough()
    {
        if (DataContext is OverlayViewModel vm)
        {
            vm.SetClickThroughIndicator(_clickThrough);
        }
    }

    /// <summary>
    /// Toggle taskbar / alt-tab mode. Implemented purely via the ex-style
    /// (APPWINDOW↔TOOLWINDOW + NOACTIVATE) so the HWND is NOT recreated (WPF's ShowInTaskbar property
    /// would recreate it and break our handle/hook). A hide+show forces the shell to re-evaluate the
    /// taskbar button after the style change.
    /// </summary>
    public void SetTaskbarMode(bool enable)
    {
        _taskbarMode = enable;
        // (Taskbar mode flips only the ex-style bucket, NOT the topmost bucket — do NOT reset
        // _presentedTopMost here. The poll's Present(true) + ReassertTopmostIfBuried keep z-order correct,
        // and not resetting avoids an extra z-order mutation/blink on the follow-up Present.)
        if (enable)
        {
            _clickThrough = false; // a taskbar/Alt+Tab window shouldn't pass input through to the game
        }

        // NOTE: visibility (Opacity) and topmost are intentionally NOT touched here — they are owned by
        // Present/Park (driven by the auto-hide poll), so taskbar mode stays orthogonal to auto-hide.
        SyncInputStyle();

        if (_handle != IntPtr.Zero)
        {
            ShowWindow(_handle, SwHide);
            ShowWindow(_handle, SwShowNoActivate);
            SyncInputStyle(); // re-assert after the show
        }

        NotifyClickThrough(); // entering taskbar mode clears click-through — keep the badge in sync
    }

    /// <summary>Show the overlay; topMost tracks whether the game is foreground. Idempotent: the 300ms
    /// auto-hide Poll calls this every tick, so re-issuing SetWindowPos when nothing changed would
    /// repeatedly dismiss hover tooltips (the reported "tooltip vanishes too fast" bug). Only re-assert
    /// z-order when the topmost state actually changed since the last Present (or after a Park).</summary>
    public void Present(bool topMost)
    {
        _parked = false; // presented: SyncInputStyle may now apply the taskbar/Alt+Tab ex-style
        Opacity = 1.0;
        Topmost = topMost;
        SyncInputStyle();
        if (_handle != IntPtr.Zero && _presentedTopMost != topMost)
        {
            SetWindowPos(_handle, topMost ? HwndTopMost : HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
            _presentedTopMost = topMost;
        }
    }

    /// <summary>Park the overlay: invisible, non-topmost, bottom of z-order (not Hide(), so the HWND
    /// + ex-style survive).</summary>
    public void Park()
    {
        _parked = true; // auto-hidden: drop the taskbar/Alt+Tab ex-style so no blank entry is left
        Opacity = 0.0;
        Topmost = false;
        _presentedTopMost = null; // force the next Present to re-assert z-order
        if (_handle != IntPtr.Zero)
        {
            SetWindowPos(_handle, HwndNoTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            SetWindowPos(_handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        SyncInputStyle();
    }

    /// <summary>
    /// Re-claim HWND_TOPMOST when a FOREIGN topmost window — a borderless-fullscreen game re-asserting its
    /// own topmost (alt-enter, resolution/HDR transitions, game-owned popups) — has climbed above us. The
    /// 300ms poll calls this while the game is foreground. It reconciles "always above the game" with "no
    /// tooltip flicker": a true no-op on the common already-on-top path (no SetWindowPos, no recomposite),
    /// re-asserts WITHOUT SwpShowWindow only when actually buried, and keeps NOACTIVATE (never steals game
    /// foreground). No tooltip guard is needed: our own ToolTip/Popup is a SAME-PROCESS topmost HWND, which the
    /// foreign-window walk skips, so a re-assert never fires merely because our tooltip is up — only a genuine
    /// foreign bury triggers SetWindowPos. (The 6361280 tooltip-dismiss regression came from the OLD
    /// unconditional per-tick HWND_TOPMOST; this buried-only walk is not that. A tooltip-open guard here used to
    /// LATCH for the 20s ShowDuration and leave the overlay stuck behind the game after a header hover.)
    /// </summary>
    public void ReassertTopmostIfBuried()
    {
        if (_handle == IntPtr.Zero || _parked || _presentedTopMost != true)
        {
            return;
        }

        if (IsBuried())
        {
            ForceTopmost();
        }
    }

    /// <summary>We intend to be topmost (caller checked) — are we actually on top? Buried if our own
    /// WS_EX_TOPMOST bit is missing (a WPF owned-window / z-order shuffle silently demoted us — the desync
    /// that left the meter pinned behind a fullscreen game), or a foreign visible window sits above us. Our
    /// own windows (tooltips / popups / sibling panels, same process) are skipped so they never trigger a
    /// needless re-assert.</summary>
    private bool IsBuried()
    {
        if ((GetWindowLong(_handle, GwlExStyle) & WsExTopmost) == 0)
        {
            return true; // the HWND lost topmost while we still think we're presented-topmost
        }

        for (IntPtr above = GetWindow(_handle, GwHwndPrev); above != IntPtr.Zero; above = GetWindow(above, GwHwndPrev))
        {
            GetWindowThreadProcessId(above, out uint pid);
            if ((int)pid != Environment.ProcessId && IsWindowVisible(above))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Force HWND_TOPMOST without show/activation (NOACTIVATE so we never steal the game's
    /// foreground). Re-topmosts any window OWNER first: an owned window cannot sit above a non-topmost owner,
    /// so a stale owner would pin us back down. We keep ShowInTaskbar at its default so there is normally NO
    /// owner (the owner step is then a no-op) — this is the belt-and-suspenders guard against any owner WPF
    /// might still attach.</summary>
    private void ForceTopmost()
    {
        IntPtr owner = GetWindow(_handle, GwOwner);
        if (owner != IntPtr.Zero)
        {
            SetWindowPos(owner, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        SetWindowPos(_handle, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private void OnDragHandle(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            PositionChanged?.Invoke(Left, Top);
        }
    }

    private void OnSettings(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void OnExit(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();

    // Header buttons: a reliable left-click path (the right-click ContextMenu is unreliable on a
    // WS_EX_NOACTIVATE window — the popup can't hold focus).
    private void OnSettingsButton(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void OnExitButton(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();

    private void OnResetButton(object sender, RoutedEventArgs e) => ResetRequested?.Invoke();

    private void OnTaskbarButton(object sender, RoutedEventArgs e) => TaskbarToggleRequested?.Invoke();

    private void OnHistoryButton(object sender, RoutedEventArgs e) => HistoryRequested?.Invoke();

    private void OnThemeButton(object sender, RoutedEventArgs e) => ThemeRequested?.Invoke();

    private void OnJoinButton(object sender, RoutedEventArgs e) => JoinRequested?.Invoke();

    private void OnUpdateButton(object sender, RoutedEventArgs e) => UpdateRequested?.Invoke();

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RowViewModel row } && DataContext is OverlayViewModel vm)
        {
            vm.ToggleSelection(row.Id);
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
}

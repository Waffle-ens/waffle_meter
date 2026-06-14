using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Shared base for secondary overlay panels (join requests, battle history). Provides the same
/// game-overlay windowing as <see cref="OverlayWindow"/> — WS_EX_NOACTIVATE|TOOLWINDOW so the panel
/// never steals focus/GPU from AION2 — plus drag-to-move, a close button, and <see cref="Present"/> /
/// <see cref="Park"/> visibility (park keeps the HWND + ex-style alive). Subclasses hook
/// <see cref="OnPresented"/> / <see cref="OnParked"/> (e.g. to run a countdown timer).
/// </summary>
public abstract class OverlayPanelWindow : Window, IReassertableOverlay
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExAppWindow = 0x00040000;
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

    private IntPtr _handle;
    private bool _dragging;
    private bool? _presentedTopMost; // last applied present state; null = parked -> ReassertTopmostIfBuried no-ops

    /// <summary>Raised after a drag completes with the new Left/Top (App persists it).</summary>
    public event Action<double, double>? PositionChanged;

    /// <summary>Raised when the user clicks ✕ (App parks the panel).</summary>
    public event Action? CloseRequested;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        // Keep ShowInTaskbar at its WPF default (true) — do NOT set it false in XAML. WPF implements
        // ShowInTaskbar=false with a hidden, non-topmost OWNER window, and an owned window can't stay topmost
        // above a borderless-fullscreen game. We hide from the taskbar via WS_EX_TOOLWINDOW below instead.
        SyncInputStyle();
        HwndSource.FromHwnd(_handle)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Skip the re-assert while a drag is in flight: WM_WINDOWPOSCHANGED fires on every move tick, and
        // re-applying the frame style mid-drag forces a repaint that reads as a flicker. Re-assert once
        // when the drag ends (OnDragHandle).
        if ((msg is WmActivate or WmWindowPosChanged) && !_dragging)
        {
            SyncInputStyle();
        }

        return IntPtr.Zero;
    }

    /// <summary>Assert the overlay ex-style (TOOLWINDOW|LAYERED|NOACTIVATE &amp; ~APPWINDOW). No-op when unchanged.</summary>
    private void SyncInputStyle()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        int current = GetWindowLong(_handle, GwlExStyle);
        int next = (current | WsExToolWindow | WsExLayered | WsExNoActivate) & ~WsExAppWindow;
        if (next == current)
        {
            return;
        }

        SetWindowLong(_handle, GwlExStyle, next);
        SetWindowPos(_handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    /// <summary>Show the panel (topMost tracks the game).</summary>
    public void Present(bool topMost)
    {
        Topmost = topMost;
        Opacity = 1.0;
        _presentedTopMost = topMost; // arm ReassertTopmostIfBuried while shown
        SyncInputStyle();
        if (_handle != IntPtr.Zero)
        {
            SetWindowPos(_handle, topMost ? HwndTopMost : HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        }

        OnPresented();
    }

    /// <summary>Hide the panel (HWND + ex-style survive).</summary>
    public void Park()
    {
        OnParked();
        Opacity = 0.0;
        Topmost = false;
        _presentedTopMost = null; // parked -> ReassertTopmostIfBuried no-ops until the next Present
        if (_handle != IntPtr.Zero)
        {
            SetWindowPos(_handle, HwndNoTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            SetWindowPos(_handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        SyncInputStyle();
    }

    /// <summary>
    /// Re-claim HWND_TOPMOST when a FOREIGN topmost window (a borderless-fullscreen game re-asserting its own
    /// topmost on alt-tab return / alt-enter / a game-owned popup) has climbed above this panel. Mirrors
    /// <see cref="OverlayWindow.ReassertTopmostIfBuried"/>: the meter's 300ms poll drives it for every open
    /// panel while AION2 is foreground, so a panel left open across an alt-tab no longer stays buried behind
    /// the game. A true no-op on the common already-on-top path (no SetWindowPos, no recomposite); re-asserts
    /// WITHOUT SwpShowWindow only when actually buried; keeps NOACTIVATE (never steals the game's foreground);
    /// and is skipped while parked or mid-drag. No tooltip guard: our own tooltip/popup is a same-process
    /// topmost HWND the walk skips, so it never triggers a re-assert (see OverlayWindow for the full rationale).
    /// </summary>
    public void ReassertTopmostIfBuried()
    {
        if (_handle == IntPtr.Zero || _presentedTopMost != true || _dragging)
        {
            return;
        }

        if (IsBuried())
        {
            ForceTopmost();
        }
    }

    /// <summary>Buried if our own WS_EX_TOPMOST bit is missing (a WPF owned-window / z-order shuffle demoted
    /// us), or a foreign visible window sits above us. Our own windows (meter / sibling panels / tooltips,
    /// same process) are skipped so they never trigger a needless re-assert.</summary>
    private bool IsBuried()
    {
        if ((GetWindowLong(_handle, GwlExStyle) & WsExTopmost) == 0)
        {
            return true;
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

    /// <summary>Force HWND_TOPMOST without show/activation (NOACTIVATE). Re-topmosts any window OWNER first so
    /// a stale non-topmost owner can't pin us below it; normally there is no owner (ShowInTaskbar default), so
    /// the owner step is a no-op.</summary>
    private void ForceTopmost()
    {
        IntPtr owner = GetWindow(_handle, GwOwner);
        if (owner != IntPtr.Zero)
        {
            SetWindowPos(owner, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        SetWindowPos(_handle, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    /// <summary>Called after the panel is presented (subclass hook, e.g. start a timer).</summary>
    protected virtual void OnPresented() { }

    /// <summary>Called before the panel is parked (subclass hook, e.g. stop a timer).</summary>
    protected virtual void OnParked() { }

    protected void OnDragHandle(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            _dragging = true;
            try
            {
                DragMove();
            }
            finally
            {
                _dragging = false;
            }

            SyncInputStyle(); // re-assert once now the drag has settled
            PositionChanged?.Invoke(Left, Top);
        }
    }

    protected void OnCloseButton(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
}

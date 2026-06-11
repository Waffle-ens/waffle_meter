using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// The party join-request overlay panel. Same game-overlay windowing as <see cref="OverlayWindow"/>
/// (WS_EX_NOACTIVATE|TOOLWINDOW so it never steals focus/GPU from the game), plus a 250ms timer that
/// drives the per-row countdown via <see cref="JoinRequestViewModel.Tick"/>. Visibility is owned by the
/// App: <see cref="Present"/> on the empty→non-empty transition, <see cref="Park"/> on close/clear.
/// </summary>
public partial class JoinRequestPanel : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExAppWindow = 0x00040000;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;

    private static readonly IntPtr HwndTopMost = new(-1);
    private static readonly IntPtr HwndTop = new(0);
    private static readonly IntPtr HwndNoTopMost = new(-2);
    private static readonly IntPtr HwndBottom = new(1);

    private const int WmActivate = 0x0006;
    private const int WmWindowPosChanged = 0x0047;

    private IntPtr _handle;
    private readonly DispatcherTimer _ticker;

    /// <summary>Raised after a drag completes with the new Left/Top (App persists it).</summary>
    public event Action<double, double>? PositionChanged;

    /// <summary>Raised when the user clicks ✕ (App parks the panel).</summary>
    public event Action? CloseRequested;

    public JoinRequestPanel()
    {
        InitializeComponent();
        _ticker = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _ticker.Tick += (_, _) => (DataContext as JoinRequestViewModel)?.Tick();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        SyncInputStyle();
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

    /// <summary>Show the panel (topMost tracks the game) and start the countdown ticker.</summary>
    public void Present(bool topMost)
    {
        Topmost = topMost;
        Opacity = 1.0;
        SyncInputStyle();
        if (_handle != IntPtr.Zero)
        {
            SetWindowPos(_handle, topMost ? HwndTopMost : HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        }

        _ticker.Start();
    }

    /// <summary>Hide the panel (HWND + ex-style survive) and stop the ticker.</summary>
    public void Park()
    {
        _ticker.Stop();
        Opacity = 0.0;
        Topmost = false;
        if (_handle != IntPtr.Zero)
        {
            SetWindowPos(_handle, HwndNoTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            SetWindowPos(_handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        SyncInputStyle();
    }

    private void OnDragHandle(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            PositionChanged?.Invoke(Left, Top);
        }
    }

    private void OnCloseButton(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}

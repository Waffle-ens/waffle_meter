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
    private bool _clickThrough;
    public bool ClickThrough => _clickThrough;

    /// <summary>Raised after a drag completes with the new Left/Top (App persists it).</summary>
    public event Action<double, double>? PositionChanged;

    /// <summary>Raised from the right-click menu (App opens the settings window / exits).</summary>
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public OverlayWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
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
        int baseStyle = (current | WsExToolWindow | WsExLayered | WsExNoActivate) & ~WsExAppWindow;
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
    }

    /// <summary>Show the overlay; topMost tracks whether the game is foreground.</summary>
    public void Present(bool topMost)
    {
        Topmost = topMost;
        Opacity = 1.0;
        SyncInputStyle();
        if (_handle != IntPtr.Zero)
        {
            SetWindowPos(_handle, topMost ? HwndTopMost : HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        }
    }

    /// <summary>Park the overlay: invisible, non-topmost, bottom of z-order (not Hide(), so the HWND
    /// + ex-style survive).</summary>
    public void Park()
    {
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

    private void OnSettings(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void OnExit(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();

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
}

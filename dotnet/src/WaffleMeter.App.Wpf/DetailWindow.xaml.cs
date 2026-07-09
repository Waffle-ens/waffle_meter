using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace WaffleMeter.App.Wpf;

public partial class DetailWindow : Window, IReassertableOverlay
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    private readonly TopmostReasserter _reasserter = new();
    private IntPtr _handle;
    private bool _dragging;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public DetailWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        // ShowInTaskbar stays at its WPF default (true) — must NOT be false in XAML, or WPF attaches a hidden
        // non-topmost OWNER window that pins this window behind a fullscreen game. WS_EX_TOOLWINDOW (below)
        // keeps it off the taskbar instead.
        int exStyle = GetWindowLong(_handle, GwlExStyle);
        SetWindowLong(_handle, GwlExStyle, exStyle | WsExNoActivate | WsExToolWindow);
    }

    /// <summary>
    /// Re-claim HWND_TOPMOST when a FOREIGN topmost window (borderless-fullscreen AION2 re-asserting its own
    /// topmost on alt-tab return) has climbed above this detail window. Driven by the meter's 300ms poll while
    /// the game is foreground, exactly like the meter and the other panels, so a detail window left open across
    /// an alt-tab no longer stays buried behind the game. A true no-op on the already-on-top path; re-asserts
    /// WITHOUT show/activation only when actually buried (NOACTIVATE — never steals the game's foreground); and
    /// is skipped while hidden/closed or mid-drag. No tooltip guard: our own tooltip/popup is a same-process
    /// topmost HWND the walk skips, so it never triggers a re-assert (see OverlayWindow for the rationale).
    /// </summary>
    public void ReassertTopmostIfBuried()
    {
        if (_handle == IntPtr.Zero || !IsVisible || _dragging)
        {
            return;
        }

        _reasserter.ReassertIfBuried(_handle);
    }

    private void OnDragHandle(object sender, MouseButtonEventArgs e)
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
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

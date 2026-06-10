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
    private const int WsExTransparent = 0x00000020;

    private bool _clickThrough;
    public bool ClickThrough => _clickThrough;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public OverlayWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // WS_EX_NOACTIVATE: the overlay never takes foreground (so it can't steal focus/FPS from the
        // game). WS_EX_TOOLWINDOW: keep it out of Alt-Tab. The key lesson from the Kotlin overlay.
        IntPtr handle = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, exStyle | WsExNoActivate | WsExToolWindow);
    }

    private void OnDragHandle(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    /// <summary>Toggle native click-through (WS_EX_TRANSPARENT): mouse passes to the game behind.</summary>
    public void SetClickThrough(bool enable)
    {
        _clickThrough = enable;
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int style = GetWindowLong(handle, GwlExStyle);
        int next = enable ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLong(handle, GwlExStyle, next);
    }
}

using System.Windows;
using System.Windows.Interop;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Adds edge/corner resize to a borderless (WindowStyle=None, AllowsTransparency) window by answering
/// WM_NCHITTEST with the matching HT* code when the cursor is within <c>margin</c> DIPs of an edge —
/// so dragging the top/bottom/left/right/corners resizes. The window must be ResizeMode=CanResize with
/// a manual size (SizeToContent=Manual); MinWidth/MinHeight bound the shrink.
/// </summary>
public static class WindowResizer
{
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtLeft = 10, HtRight = 11, HtTop = 12, HtTopLeft = 13, HtTopRight = 14,
                      HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17;

    public static void Attach(Window window, double margin = 6)
    {
        window.SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(window) is HwndSource source)
            {
                source.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                    WndProc(window, margin, msg, lParam, ref handled));
            }
        };
    }

    private static IntPtr WndProc(Window window, double margin, int msg, IntPtr lParam, ref bool handled)
    {
        if (msg != WmNcHitTest)
        {
            return IntPtr.Zero;
        }

        int lp = lParam.ToInt32();
        var screenPoint = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)); // physical px
        Point p = window.PointFromScreen(screenPoint);                                   // DIPs from window top-left
        double w = window.ActualWidth, h = window.ActualHeight;
        if (p.X < 0 || p.Y < 0 || p.X > w || p.Y > h)
        {
            return IntPtr.Zero;
        }

        bool left = p.X <= margin, right = p.X >= w - margin;
        bool top = p.Y <= margin, bottom = p.Y >= h - margin;
        int code = (left, right, top, bottom) switch
        {
            (true, _, true, _) => HtTopLeft,
            (_, true, true, _) => HtTopRight,
            (true, _, _, true) => HtBottomLeft,
            (_, true, _, true) => HtBottomRight,
            (true, _, _, _) => HtLeft,
            (_, true, _, _) => HtRight,
            (_, _, true, _) => HtTop,
            (_, _, _, true) => HtBottom,
            _ => HtClient,
        };

        if (code == HtClient)
        {
            return IntPtr.Zero; // let the normal client hit-testing (drag handle, buttons) run
        }

        handled = true;
        return (IntPtr)code;
    }
}

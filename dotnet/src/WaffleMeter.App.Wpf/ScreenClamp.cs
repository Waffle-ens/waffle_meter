using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Keeps a window fully on-screen when multi-monitor movement is OFF: clamps its bounds to the work
/// area of the monitor it currently overlaps, so it can't leave that monitor or go off-screen. When ON,
/// movement is unconstrained (panels/overlay can travel to secondary monitors). The toast is never
/// clamped (it owns its bottom-right placement).
/// </summary>
public static class ScreenClamp
{
    public static void Apply(Window window, bool allowMultiMonitor)
    {
        if (allowMultiMonitor || window.ActualWidth <= 0 || window.ActualHeight <= 0)
        {
            return;
        }

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // Full monitor bounds (not WorkingArea) so a window can travel to the true screen edges on every
        // side — incl. down past the taskbar. WorkingArea stops short of the taskbar, which read as the
        // window "moving less" at the bottom while top/left/right reached the edge. The game overlay is
        // meant to sit anywhere on the monitor (the game is usually fullscreen/taskbar-hidden anyway).
        System.Drawing.Rectangle wa = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds; // physical px
        DpiScale dpi = VisualTreeHelper.GetDpi(window);
        double left = wa.Left / dpi.DpiScaleX;
        double top = wa.Top / dpi.DpiScaleY;
        double right = wa.Right / dpi.DpiScaleX;
        double bottom = wa.Bottom / dpi.DpiScaleY;

        double maxX = Math.Max(left, right - window.ActualWidth);
        double maxY = Math.Max(top, bottom - window.ActualHeight);
        double newLeft = Math.Clamp(window.Left, left, maxX);
        double newTop = Math.Clamp(window.Top, top, maxY);

        if (Math.Abs(newLeft - window.Left) > 0.5)
        {
            window.Left = newLeft;
        }

        if (Math.Abs(newTop - window.Top) > 0.5)
        {
            window.Top = newTop;
        }
    }
}

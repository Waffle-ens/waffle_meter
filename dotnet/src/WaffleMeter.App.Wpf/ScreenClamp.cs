using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Keeps a window on-screen. Multi-monitor OFF: clamps to the bounds of the monitor it currently
/// overlaps, so it can't leave that monitor. Multi-monitor ON: clamps to the union of all monitors (the
/// virtual desktop) so it may travel to any monitor but never into dead space outside them all. Either
/// way a restored/dragged position stays reachable. The toast is never clamped (it owns its placement).
/// </summary>
public static class ScreenClamp
{
    public static void Apply(Window window, bool allowMultiMonitor)
    {
        if (window.ActualWidth <= 0 || window.ActualHeight <= 0)
        {
            return;
        }

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // Off-screen guard. Full monitor bounds (not WorkingArea) so a window can travel to the true
        // screen edges on every side — incl. down past the taskbar (the game overlay is meant to sit
        // anywhere on the monitor; the game is usually fullscreen/taskbar-hidden anyway).
        //  - multi-monitor OFF: confine to the single monitor the window currently sits on.
        //  - multi-monitor ON : confine only to the UNION of all monitors (the virtual desktop), so it
        //    may live on any monitor but can never be dragged/restored into dead space outside them all
        //    (mirrors the Kotlin virtualScreenBounds clamp — a stored position stays reachable).
        System.Drawing.Rectangle wa = allowMultiMonitor
            ? System.Windows.Forms.SystemInformation.VirtualScreen
            : System.Windows.Forms.Screen.FromHandle(hwnd).Bounds; // physical px
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

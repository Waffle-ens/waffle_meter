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
    /// <param name="anchor">Optional point (DIP) that decides WHICH monitor confines the window, instead of
    /// the window's own rectangle. Needed for a window that grows on its own: once it is wide enough to
    /// straddle two monitors, <c>Screen.FromHandle</c> picks whichever holds the LARGER slice — the
    /// neighbour — and the clamp then shoves the window onto that monitor instead of pulling it back onto
    /// the one the user parked it on. Ignored while multi-monitor movement is on (the union has no such
    /// ambiguity).</param>
    public static void Apply(Window window, bool allowMultiMonitor, Point? anchor = null)
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
        DpiScale dpi = VisualTreeHelper.GetDpi(window);
        System.Drawing.Rectangle wa = allowMultiMonitor
            ? System.Windows.Forms.SystemInformation.VirtualScreen
            : MonitorBounds(hwnd, anchor, dpi); // physical px
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

    /// <summary>The confining monitor: the one holding <paramref name="anchor"/> when given, else the one the
    /// window mostly overlaps. Full bounds, not WorkingArea — see <see cref="Apply"/>.</summary>
    private static System.Drawing.Rectangle MonitorBounds(IntPtr hwnd, Point? anchor, DpiScale dpi) =>
        anchor is { } a
            ? System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point((int)(a.X * dpi.DpiScaleX), (int)(a.Y * dpi.DpiScaleY))).Bounds
            : System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;

    /// <summary>Work area (taskbar excluded) of the monitor holding <paramref name="anchor"/>, in DIP. Used to
    /// cap the width of a SizeToContent window — WPF truncates such a window's measurement at the work-area
    /// width and drops whatever overflowed, with no wrap and no scroll.</summary>
    public static double WorkAreaWidth(Window window, Point anchor)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(window);
        System.Drawing.Rectangle wa = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)(anchor.X * dpi.DpiScaleX), (int)(anchor.Y * dpi.DpiScaleY))).WorkingArea;
        return wa.Width / dpi.DpiScaleX;
    }
}

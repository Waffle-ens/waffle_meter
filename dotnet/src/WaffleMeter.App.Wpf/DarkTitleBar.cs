using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Win10 1809+ / Win11 immersive dark title bar, so a window's OS chrome matches its dark client area
/// instead of sitting under a white strip. Every normal (non-overlay) window in the app uses this — the
/// settings window, the field-boss picker, the replay player — so they all read as the same app.
/// </summary>
public static class DarkTitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Apply once the window has an HWND. Safe to call from a ctor.</summary>
    public static void Apply(Window window) => window.SourceInitialized += (_, _) => Enable(window);

    private static void Enable(Window window)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            int on = 1;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (19 on older builds); try both.
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
            }
        }
        catch
        {
            // older OS without the dwmapi attribute — light title bar, harmless
        }
    }
}

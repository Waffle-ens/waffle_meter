using System.Windows;
using System.Windows.Interop;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Adds edge/corner resize to a borderless (WindowStyle=None, AllowsTransparency) window by answering
/// WM_NCHITTEST with the matching HT* code when the cursor is within <c>margin</c> DIPs of an edge —
/// so dragging the top/bottom/left/right/corners resizes. The window must be ResizeMode=CanResize with
/// a manual size (SizeToContent=Manual); MinWidth/MinHeight bound the shrink.
/// <para><paramref name="widthOnly"/> (the meter overlay): the left/right edges — INCLUDING the corners —
/// map to a horizontal (width) resize, and the top/bottom-only zones do nothing. So the invisible edge/corner
/// drag still resizes, but only the WIDTH. The meter keeps <c>SizeToContent="Height"</c> so its height
/// auto-fits the row count; exposing a vertical/diagonal handle would let a drag flip SizeToContent to Manual
/// and freeze the height (re-introducing the "10인 공대에서 아래 행이 잘린다" 회귀).</para>
/// </summary>
public static class WindowResizer
{
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtLeft = 10, HtRight = 11, HtTop = 12, HtTopLeft = 13, HtTopRight = 14,
                      HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17;

    public static void Attach(Window window, double margin = 6, bool widthOnly = false)
    {
        // The window may already be shown (Attach is called after Show), in which case SourceInitialized
        // has fired — add the hook now; otherwise wait for it.
        if (PresentationSource.FromVisual(window) is HwndSource existing)
        {
            AddHook(existing, window, margin, widthOnly);
        }
        else
        {
            window.SourceInitialized += (_, _) =>
            {
                if (PresentationSource.FromVisual(window) is HwndSource source)
                {
                    AddHook(source, window, margin, widthOnly);
                }
            };
        }
    }

    private static void AddHook(HwndSource source, Window window, double margin, bool widthOnly) =>
        source.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            WndProc(window, margin, widthOnly, msg, lParam, ref handled));

    private static IntPtr WndProc(Window window, double margin, bool widthOnly, int msg, IntPtr lParam, ref bool handled)
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
        int code;
        if (widthOnly)
        {
            // 미터: 좌/우 가장자리(세로 전 구간 + 모서리 포함)는 가로 리사이즈, 그 밖(상/하 중앙)은 리사이즈 안 함.
            // 세로/대각 핸들을 노출하지 않으므로 SizeToContent="Height"(행 수 자동추종)가 절대 Manual로 뒤집히지
            // 않는다 — 모서리를 잡아 끌면 폭이 조절되고 높이는 자동 그대로다.
            code = left ? HtLeft : right ? HtRight : HtClient;
        }
        else
        {
            bool top = p.Y <= margin, bottom = p.Y >= h - margin;
            code = (left, right, top, bottom) switch
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
        }

        if (code == HtClient)
        {
            return IntPtr.Zero; // let the normal client hit-testing (drag handle, buttons) run
        }

        handled = true;
        return (IntPtr)code;
    }
}

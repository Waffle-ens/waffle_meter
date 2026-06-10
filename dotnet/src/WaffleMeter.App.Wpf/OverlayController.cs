using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using WaffleMeter.Services;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Drives overlay visibility (Kotlin BrowserApp auto-hide). A 300ms poll presents the overlay only
/// when AION2 (or this app) is foreground and parks it otherwise; auto-hide off keeps it always
/// present with topmost tracking the game. Visibility toggle / tray hide use park (not Hide) so the
/// HWND + ex-style survive. All window mutations happen on the UI thread (DispatcherTimer / callers).
/// </summary>
public sealed class OverlayController
{
    private const string AionExe = "Aion2.exe";
    private const uint ProcessQueryLimitedInformation = 0x1000;

    private readonly OverlayWindow _window;
    private readonly PropertyHandler _props;
    private readonly DispatcherTimer _timer;
    private bool _aionEverFocused;

    public OverlayController(OverlayWindow window, PropertyHandler props)
    {
        _window = window;
        _props = props;
        IsAutoHide = props.GetProperty("isAutoHide") is not "false"; // default true
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += (_, _) => Poll();
    }

    public bool IsVisible { get; private set; } = true;
    public bool IsAutoHide { get; private set; }

    public void Start() => _timer.Start();

    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            HideToTray();
        }
        else
        {
            ShowFromTray();
        }
    }

    public void HideToTray()
    {
        IsVisible = false;
        _window.Park();
    }

    public void ShowFromTray()
    {
        IsVisible = true;
        _aionEverFocused = false; // re-arm: don't present until AION2 is seen again
        _window.Present(true);
    }

    /// <summary>Tray "input recover": force the overlay back on top, interactive.</summary>
    public void Present() => _window.Present(true);

    public void SetAutoHide(bool enabled)
    {
        IsAutoHide = enabled;
        _props.SetProperty("isAutoHide", enabled ? "true" : "false");
    }

    private void Poll()
    {
        if (!IsVisible)
        {
            return; // parked/hidden owns visibility while hidden
        }

        bool aionFocused = ForegroundExeEndsWith(AionExe);
        bool selfFocused = ForegroundProcessId() == Environment.ProcessId;

        if (!IsAutoHide)
        {
            _window.Present(aionFocused); // always shown; topmost follows the game
            return;
        }

        if (!_aionEverFocused)
        {
            if (aionFocused)
            {
                _aionEverFocused = true;
            }
            else
            {
                return; // don't park before the game has ever been focused
            }
        }

        if (aionFocused || selfFocused)
        {
            _window.Present(aionFocused);
        }
        else
        {
            _window.Park();
        }
    }

    private static int ForegroundProcessId()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return 0;
        }

        GetWindowThreadProcessId(hwnd, out uint pid);
        return (int)pid;
    }

    private static bool ForegroundExeEndsWith(string exe)
    {
        int pid = ForegroundProcessId();
        if (pid == 0)
        {
            return false;
        }

        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            if (QueryFullProcessImageName(handle, 0, buffer, ref size))
            {
                return buffer.ToString().EndsWith(exe, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}

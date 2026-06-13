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
    private uint _aionPid; // cached AION2 foreground PID; keeps the game recognized even when a
                           // protected/anti-cheat process can no longer be OpenProcess'd to read its name
    private int _parkPending; // consecutive "another app is foreground" polls before we actually park
    // ~0.9s at the 300ms cadence: a short grace so brief focus excursions (game-owned popups, a quick
    // alt-tab, the capture-helper UAC prompt) don't flicker the overlay (mirrors React's ~1s debounce).
    private const int ParkGraceTicks = 3;

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
    public bool TaskbarMode { get; private set; }

    public void Start() => _timer.Start();

    /// <summary>Toggle taskbar/Alt+Tab mode. This ONLY controls whether the overlay is listed in the
    /// taskbar/Alt+Tab (the window ex-style) — it is ORTHOGONAL to auto-hide. The overlay's on-screen
    /// visibility is always governed by the auto-hide poll ("아이온 활성화 시 표시"), in BOTH taskbar modes,
    /// so the two options no longer fight (the old code suspended the poll + forced the window visible).
    /// Re-poll immediately so the new ex-style + correct visibility apply at once.</summary>
    public void SetTaskbarMode(bool enable)
    {
        TaskbarMode = enable;
        _window.SetTaskbarMode(enable);
        Poll();
    }

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

        Foreground fg = DetectForeground();

        if (!IsAutoHide)
        {
            // "항상 표시": hold HWND_TOPMOST regardless of foreground. (The old Present(fg == Aion) demoted to
            // non-topmost on every Self/Other/Unknown excursion, thrashing z-order = the intermittent flicker.)
            _window.Present(true);
            _window.ReassertTopmostIfBuried(); // re-claim if a borderless game re-asserted its own topmost above us
            return;
        }

        if (!_aionEverFocused)
        {
            if (fg == Foreground.Aion)
            {
                _aionEverFocused = true;
            }
            else
            {
                return; // startup grace: don't park before the game has ever been focused
            }
        }

        switch (fg)
        {
            case Foreground.Aion:
                _parkPending = 0;
                _window.Present(true);
                _window.ReassertTopmostIfBuried(); // re-claim if the game re-topped above us
                break;
            case Foreground.Self:
                _parkPending = 0;
                _window.Present(false);
                break;
            case Foreground.Unknown:
                // Ambiguous: no foreground window (alt-tab / UAC secure desktop / lock), or an
                // unqueryable process that isn't the known game PID. Don't act on a non-answer —
                // HOLD the current present/park state rather than wrongly parking.
                break;
            default: // Foreground.Other: a different app is genuinely foreground
                // Park after a short grace so brief excursions don't flicker the overlay, and re-issue
                // the park only ONCE when the grace elapses (not every tick).
                if (_parkPending < ParkGraceTicks && ++_parkPending == ParkGraceTicks)
                {
                    _window.Park();
                }

                break;
        }
    }

    private enum Foreground
    {
        Aion,    // the game is foreground (confirmed by exe name, or by the cached game PID)
        Self,    // this app (overlay / settings / panels) is foreground
        Other,   // a different, queryable process is foreground
        Unknown, // can't tell: no foreground window, or an unqueryable (protected) non-cached process
    }

    /// <summary>Classify the foreground window. The game PID is cached on the first confirmed name match
    /// so the game stays recognized even when anti-cheat later blocks OpenProcess (a denied query is
    /// treated as Unknown, never as a false "not the game"); a transient null foreground is likewise
    /// Unknown rather than a false negative that would wrongly park the overlay mid-fight.</summary>
    private Foreground DetectForeground()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return Foreground.Unknown;
        }

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
        {
            return Foreground.Unknown;
        }

        if ((int)pid == Environment.ProcessId)
        {
            return Foreground.Self;
        }

        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
        {
            // Protected / anti-cheat process we can't open: trust the cached game PID, else Unknown.
            return pid == _aionPid ? Foreground.Aion : Foreground.Unknown;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            if (!QueryFullProcessImageName(handle, 0, buffer, ref size))
            {
                return pid == _aionPid ? Foreground.Aion : Foreground.Unknown;
            }

            if (buffer.ToString().EndsWith(AionExe, StringComparison.OrdinalIgnoreCase))
            {
                _aionPid = pid; // remember the game PID for the anti-cheat-denial path above
                return Foreground.Aion;
            }

            if (pid == _aionPid)
            {
                _aionPid = 0; // the cached PID was reused by a non-game process; forget it
            }

            return Foreground.Other;
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

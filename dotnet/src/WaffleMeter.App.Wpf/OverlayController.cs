using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using WaffleMeter.Services;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Drives overlay visibility (Kotlin BrowserApp auto-hide). A 300ms poll keeps the overlay permanently
/// topmost and only modulates its Opacity: it is shown (opaque) when AION2 (or this app) is foreground and
/// FADED (opacity 0 + click-through, still topmost) otherwise, so returning to the game is a single
/// Opacity flip with no z-order reclaim race. Auto-hide off keeps it always shown.
/// Visibility toggle / tray hide also fade (not Hide) so the HWND + ex-style survive. All window mutations
/// happen on the UI thread (DispatcherTimer / callers).
/// </summary>
public sealed class OverlayController
{
    private const string AionExe = "Aion2.exe";
    private const uint ProcessQueryLimitedInformation = 0x1000;

    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutofcontext = 0x0000;
    private const uint WineventSkipownprocess = 0x0002;

    private readonly OverlayWindow _window;
    private readonly PropertyHandler _props;
    private readonly DispatcherTimer _timer;
    private readonly List<IReassertableOverlay> _overlays = new(); // panels (join/history/skill/toast) + per-row detail
    private readonly WinEventDelegate _winEventProc; // field-held so the marshaled callback isn't GC'd
    private IntPtr _winEventHook;
    private bool _aionEverFocused;
    private uint _aionPid; // cached AION2 foreground PID; keeps the game recognized even when a
                           // protected/anti-cheat process can no longer be OpenProcess'd to read its name
    private int _parkPending; // consecutive "another app is foreground" polls before we actually park
    // ~0.9s at the 300ms cadence: a short grace so brief focus excursions (game-owned popups, a quick
    // alt-tab, the capture-helper UAC prompt) don't flicker the overlay (mirrors React's ~1s debounce).
    private const int ParkGraceTicks = 3;

    // Diagnostics: DetectForeground records WHY it classified the foreground, so the de-duplicated
    // LogDiag line can pinpoint the "returned to the game but the overlay stayed parked" case in the
    // wild (DetectForeground returning Unknown/Other for AION2). See
    // overlay-autohide-unpark-on-return-rootcause. Captured every classification, logged only on change.
    private uint _diagFgPid;
    private bool _diagOpenProcOk;   // OpenProcess succeeded (false = anti-cheat / protected denial)
    private string _diagFgExe = ""; // foreground exe filename when readable, else ""
    private bool _diagReacquireHit; // IsAionPid resolved via GetProcessesByName("Aion2"), not the cache
    private string _lastDiagLine = "";

    public OverlayController(OverlayWindow window, PropertyHandler props)
    {
        _window = window;
        _props = props;
        IsAutoHide = props.GetProperty("isAutoHide") is not "false"; // default true
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += (_, _) => Poll();
        _winEventProc = OnForegroundChanged;
    }

    public bool IsVisible { get; private set; } = true;
    public bool IsAutoHide { get; private set; }
    public bool TaskbarMode { get; private set; }

    /// <summary>True while the meter is actually on screen (game foreground + not tray-hidden). Secondary
    /// overlays (e.g. the combat-assist buff overlay) mirror this so they hide when the game loses focus.</summary>
    public bool MeterShown { get; private set; } = true;

    // Companion overlay (the combat-assist buff overlay): presented/parked in exact lockstep with the meter
    // window, gated by its enabled predicate — so when the toggle is on it is ALWAYS shown whenever the meter
    // is, and never disappears on its own. Edge-tracked so a steady state doesn't re-issue SetWindowPos.
    private OverlayPanelWindow? _companion;
    private Func<bool>? _companionEnabled;
    private bool? _companionShown;

    /// <summary>Register a companion overlay to present/park in lockstep with the meter (gated by
    /// <paramref name="enabled"/>). Unlike a plain registered overlay, its visibility fully tracks the meter.</summary>
    public void SetCompanion(OverlayPanelWindow overlay, Func<bool> enabled)
    {
        _companion = overlay;
        _companionEnabled = enabled;
    }

    private void SyncCompanion(bool meterPresent)
    {
        if (_companion is null)
        {
            return;
        }

        bool want = meterPresent && _companionEnabled?.Invoke() == true;
        if (_companionShown == want)
        {
            return;
        }

        _companionShown = want;
        if (want)
        {
            _companion.Present(true);
        }
        else
        {
            _companion.Park();
        }
    }

    public void Start()
    {
        _timer.Start();
        // Re-claim topmost the INSTANT AION2 regains the foreground (alt-tab return / app switch) instead of
        // waiting up to one 300ms poll tick — the user-visible "돌아오면 즉시 최상단" behavior. The poll stays as
        // the backstop for the case the hook can't see: the game re-asserting its OWN topmost while it KEEPS
        // the foreground (no foreground-change event fires then).
        _winEventHook = SetWinEventHook(
            EventSystemForeground, EventSystemForeground, IntPtr.Zero, _winEventProc, 0, 0,
            WineventOutofcontext | WineventSkipownprocess);
    }

    /// <summary>Stop the poll and unhook the foreground listener (app shutdown).</summary>
    public void Stop()
    {
        _timer.Stop();
        if (_winEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
    }

    // EVENT_SYSTEM_FOREGROUND callback. Fires on the UI thread (OUTOFCONTEXT delivers to the installing
    // thread's message loop), so it can touch windows directly. Whenever AION2 becomes the foreground window
    // and we're not tray-hidden, force the overlay (and any open panel/detail window) back on top at once.
    private void OnForegroundChanged(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (!IsVisible || DetectForeground() != Foreground.Aion)
            {
                return; // tray-hidden, or something other than the game took focus
            }

            _parkPending = 0;
            _window.Present();                 // un-fade if auto-hidden, then...
            _window.ReassertTopmostIfBuried(); // ...re-claim above the topmost the game just re-asserted
            ReassertOverlaysIfBuried();
            SyncCompanion(true);               // the buff overlay returns with the meter on alt-tab return
        }
        catch
        {
            // a WinEvent callback must never throw into the message loop
        }
    }

    /// <summary>Register a secondary overlay (join requests / battle history / skill flyout / update toast /
    /// per-row detail window) so the poll re-claims its topmost whenever AION2 returns to the foreground and
    /// re-asserts its own topmost above us — e.g. an overlay left open across an alt-tab. The overlay self-skips
    /// when it is parked/hidden or already on top, so registering one that is currently hidden is harmless.</summary>
    public void RegisterOverlay(IReassertableOverlay overlay)
    {
        if (!_overlays.Contains(overlay))
        {
            _overlays.Add(overlay);
        }
    }

    /// <summary>Stop polling an overlay. The per-row detail window is recreated/closed per click, so it
    /// unregisters on Closed to keep dead-HWND references from accumulating in the list.</summary>
    public void UnregisterOverlay(IReassertableOverlay overlay) => _overlays.Remove(overlay);

    // Re-claim topmost for every registered overlay that is open. Called only on the topmost-present paths
    // (game foreground / always-show), mirroring the meter window's own ReassertTopmostIfBuried.
    private void ReassertOverlaysIfBuried()
    {
        foreach (IReassertableOverlay overlay in _overlays)
        {
            overlay.ReassertTopmostIfBuried();
        }
    }

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
        // Branch on the ACTUAL on-screen state (parked = invisible), not the IsVisible tray-bool. The
        // auto-hide poll can Park() the window while IsVisible stays true, and in always-on mode the poll
        // early-returns on !IsVisible so it can't repair a bool the hotkey left out of step — keying off the
        // window's real park state makes one press always do the visible-correct thing, so Ctrl+H can't get
        // stuck "hidden" (the reported "hide, then the same key won't bring it back" bug).
        if (_window.DiagParked)
        {
            ShowFromTray();
        }
        else
        {
            HideToTray();
        }
    }

    public void HideToTray()
    {
        IsVisible = false;
        _window.Fade();
    }

    public void ShowFromTray()
    {
        IsVisible = true;
        if (IsAutoHide)
        {
            // Auto-hide only: re-arm the startup grace so the poll holds the just-shown overlay present until
            // AION2 is next seen, instead of parking it after the grace just because another app is foreground
            // right now. In always-on mode the poll never reads _aionEverFocused, so leaving it untouched
            // there keeps Show idempotent — the always-on "show" path no longer depends on this flag.
            _aionEverFocused = false;
        }

        _window.Present();
    }

    /// <summary>Tray "input recover": force the overlay back on top, interactive.</summary>
    public void Present() => _window.Present();

    public void SetAutoHide(bool enabled)
    {
        IsAutoHide = enabled;
        _props.SetProperty("isAutoHide", enabled ? "true" : "false");
    }

    private void Poll()
    {
        Foreground fg = DetectForeground();
        LogDiag(fg); // de-duplicated foreground-state trace (logged before the IsVisible gate so a
                     // tray-hidden episode is captured too — distinguishes auto-hide-park from IsVisible=false)

        if (!IsVisible)
        {
            MeterShown = false;
            SyncCompanion(false); // tray-hidden → the buff overlay hides with the meter
            return; // parked/hidden owns visibility while hidden
        }

        if (!IsAutoHide)
        {
            MeterShown = true;
            // "항상 표시": hold HWND_TOPMOST regardless of foreground. (The old Present(fg == Aion) demoted to
            // non-topmost on every Self/Other/Unknown excursion, thrashing z-order = the intermittent flicker.)
            _window.Present();
            _window.ReassertTopmostIfBuried(); // re-claim if a borderless game re-asserted its own topmost above us
            ReassertOverlaysIfBuried();        // ...and any open panel/detail window buried the same way (alt-tab return)
            SyncCompanion(true);
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
                MeterShown = true;
                _window.Present();
                _window.ReassertTopmostIfBuried(); // re-claim if the game re-topped above us
                ReassertOverlaysIfBuried();        // ...and any open panel/detail window (e.g. left open across an alt-tab)
                SyncCompanion(true);
                break;
            case Foreground.Self:
                // Our own window (settings / a panel / the meter's own popup) is foreground. Keep the overlay
                // shown and topmost — do NOT demote. The old Present(false) demotion was a reclaim-race source;
                // owned dialogs (Owner = meter) already render above the topmost meter, so nothing is covered.
                _parkPending = 0;
                MeterShown = true;
                _window.Present();
                _window.ReassertTopmostIfBuried();
                ReassertOverlaysIfBuried();
                SyncCompanion(true);
                break;
            case Foreground.Unknown:
                // Ambiguous: no foreground window (alt-tab / UAC secure desktop / lock), or an
                // unqueryable process that isn't the known game PID. Don't act on a non-answer —
                // HOLD the current present/park state rather than wrongly parking.
                break;
            default: // Foreground.Other: a different app is genuinely foreground
                // Fade (opacity 0, stays topmost) after a short grace so brief excursions don't flicker the
                // overlay, and re-issue the fade only ONCE when the grace elapses (Fade is itself idempotent).
                if (_parkPending < ParkGraceTicks && ++_parkPending == ParkGraceTicks)
                {
                    MeterShown = false;
                    _window.Fade();
                    SyncCompanion(false); // the buff overlay fades off screen together with the meter
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
        // Reset the per-classification diagnostics (LogDiag reads these after we return).
        _diagFgPid = 0;
        _diagOpenProcOk = false;
        _diagFgExe = "";
        _diagReacquireHit = false;

        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return Foreground.Unknown;
        }

        GetWindowThreadProcessId(hwnd, out uint pid);
        _diagFgPid = pid;
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
            return IsAionPid(pid) ? Foreground.Aion : Foreground.Unknown;
        }

        _diagOpenProcOk = true;
        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            if (!QueryFullProcessImageName(handle, 0, buffer, ref size))
            {
                return IsAionPid(pid) ? Foreground.Aion : Foreground.Unknown;
            }

            string image = buffer.ToString();
            _diagFgExe = Path.GetFileName(image);
            if (image.EndsWith(AionExe, StringComparison.OrdinalIgnoreCase))
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

    /// <summary>Append one foreground-state line whenever the classification (or the inputs that decide
    /// it) change, so the NEXT in-the-wild repro of "returned to the game but the overlay stayed parked"
    /// records exactly why DetectForeground did not return Aion (e.g. <c>detect=Unknown openProc=False
    /// reAcquireHit=False fgPid=&lt;game&gt;</c>). De-duplicated (tiny on disk), size-capped, and never
    /// throws into the poll. Lands next to settings.properties as <c>overlay-diag.log</c>.</summary>
    private void LogDiag(Foreground fg)
    {
        try
        {
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "detect={0} isVisible={1} parked={2} topmost={3} autoHide={4} fgPid={5} fgExe={6} openProc={7} aionPidCache={8} reAcquireHit={9}",
                fg, IsVisible, _window.DiagParked, _window.DiagTopmost, IsAutoHide, _diagFgPid,
                string.IsNullOrEmpty(_diagFgExe) ? "?" : _diagFgExe,
                _diagOpenProcOk, _aionPid, _diagReacquireHit);
            if (line == _lastDiagLine)
            {
                return; // unchanged since the last tick — keep the log small
            }

            _lastDiagLine = line;
            string path = DiagLogPath();
            if (new FileInfo(path) is { Exists: true, Length: > 1_000_000 })
            {
                return; // safety cap: stop before the trace grows without bound
            }

            File.AppendAllText(
                path,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + line + Environment.NewLine);
        }
        catch
        {
            // diagnostics must never disturb the poll
        }
    }

    private static string DiagLogPath()
    {
        string appData = Environment.GetEnvironmentVariable("APPDATA")
                         ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(appData, "waffle_meter.v1.4", "overlay-diag.log");
    }

    /// <summary>True if <paramref name="pid"/> is the AION2 game. Trusts the cached <see cref="_aionPid"/>
    /// first; if that was cleared (a long alt-tab absence lets the OS reuse the game's old PID for another
    /// app, clearing the cache at ~line 202-205), RE-ACQUIRE it by enumerating live processes named
    /// "Aion2". GetProcessesByName reads the process-name snapshot (no OpenProcess handle), so it still
    /// works when anti-cheat denies a handle — which is exactly the return-from-long-absence case where the
    /// game would otherwise stay classified Unknown (and the parked overlay stay hidden) indefinitely.</summary>
    private bool IsAionPid(uint pid)
    {
        if (pid != 0 && pid == _aionPid)
        {
            return true;
        }

        try
        {
            System.Diagnostics.Process[] procs = System.Diagnostics.Process.GetProcessesByName("Aion2");
            try
            {
                foreach (System.Diagnostics.Process p in procs)
                {
                    if ((uint)p.Id == pid)
                    {
                        _aionPid = pid; // re-cache so subsequent denied ticks resolve instantly
                        _diagReacquireHit = true; // resolved by live enumeration, not the cache
                        return true;
                    }
                }
            }
            finally
            {
                foreach (System.Diagnostics.Process p in procs)
                {
                    p.Dispose(); // GetProcessesByName hands back open handles — release every one
                }
            }
        }
        catch
        {
            // enumeration can throw on access / an exited-process race — treat as not-the-game
        }

        return false;
    }

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

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

using System.Runtime.InteropServices;
using WaffleMeter.Services;

namespace WaffleMeter.App.Core;

/// <summary>A modifier + virtual-key combo (Kotlin HotkeyHandler.HotkeyCombo). Persisted as
/// "<c>modifiers=M,vkCode=V</c>".</summary>
public sealed record HotkeyCombo(int Modifiers, int VkCode)
{
    public override string ToString() => $"modifiers={Modifiers},vkCode={VkCode}";

    public static HotkeyCombo? TryParse(string s)
    {
        try
        {
            var map = new Dictionary<string, int>();
            foreach (string part in s.Split(','))
            {
                string[] kv = part.Split('=');
                if (kv.Length != 2)
                {
                    return null;
                }

                map[kv[0].Trim()] = int.Parse(kv[1].Trim());
            }

            if (!map.TryGetValue("modifiers", out int modifiers) || !map.TryGetValue("vkCode", out int vkCode))
            {
                return null;
            }

            return new HotkeyCombo(modifiers, vkCode);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Verbatim port of Kotlin <c>config.HotkeyHandler</c>: registers up to three global hotkeys (reset
/// combat / toggle visibility / toggle click-through) and pumps a message loop on a dedicated thread,
/// dispatching to callbacks. Combos persist via <see cref="PropertyHandler"/> (keys hotkey/hideHotkey/
/// clickThroughHotkey) with the same defaults (Ctrl+R / Ctrl+H / Ctrl+T). Any combo may be left
/// unassigned (<c>null</c>, persisted as "<c>none</c>"), in which case no global hotkey is registered
/// for that action. The WPF app wires the callbacks to overlay actions.
/// </summary>
public sealed class HotkeyHandler : IDisposable
{
    public const int ModAlt = 0x0001;
    public const int ModControl = 0x0002;
    public const int ModShift = 0x0004;
    public const int ModWin = 0x0008;

    private const uint WmHotkey = 0x0312;
    private const uint PmRemove = 0x0001;
    private const int ResetId = 1;
    private const int VisibilityId = 2;
    private const int ClickThroughId = 3;

    // A held global hotkey auto-repeats: Windows posts WM_HOTKEY at the keyboard repeat rate while the combo
    // stays down. For a TOGGLE action (Ctrl+H visibility) an even-count repeat burst cancels itself out and
    // leaves the overlay in the wrong state — the "hide, then the same key won't bring it back" report.
    // Collapse a burst into a single action: fire only on the leading edge, ignoring further WM_HOTKEY for
    // the same id until that id's stream has been quiet for this long (a fresh press past the gap fires again).
    private const long HotkeyRepeatSuppressMs = 400;

    private const string KeyReset = "hotkey";
    private const string KeyVisibility = "hideHotkey";
    private const string KeyClickThrough = "clickThroughHotkey";

    // Persisted marker for an explicitly-unassigned hotkey. Distinct from "property never set" (→ default)
    // and from a corrupt/unparseable value (→ default): an unassigned combo registers no global hotkey.
    private const string NoneSentinel = "none";

    private static readonly HotkeyCombo DefaultReset = new(ModControl, 0x52);        // Ctrl+R
    private static readonly HotkeyCombo DefaultVisibility = new(ModControl, 0x48);   // Ctrl+H
    private static readonly HotkeyCombo DefaultClickThrough = new(ModControl, 0x54); // Ctrl+T

    private readonly PropertyHandler _props;
    private volatile HotkeyCombo? _reset;
    private volatile HotkeyCombo? _visibility;
    private volatile HotkeyCombo? _clickThrough;
    private Thread? _listener;
    private volatile bool _running;
    private readonly Dictionary<int, long> _lastHotkeyTick = new(); // per-id leading-edge debounce; listener-thread-only

    public Action? OnReset { get; set; }
    public Action? OnVisibility { get; set; }
    public Action? OnClickThrough { get; set; }

    public HotkeyHandler(PropertyHandler props)
    {
        _props = props;
        _reset = Load(KeyReset, DefaultReset);
        _visibility = Load(KeyVisibility, DefaultVisibility);
        _clickThrough = Load(KeyClickThrough, DefaultClickThrough);
    }

    public HotkeyCombo? Reset => _reset;
    public HotkeyCombo? Visibility => _visibility;
    public HotkeyCombo? ClickThrough => _clickThrough;

    /// <summary>Set (or with <c>null</c>, unassign) the reset hotkey; persists and re-registers live.</summary>
    public void SetReset(HotkeyCombo? combo) => Update(v => _reset = v, KeyReset, combo);
    public void SetVisibility(HotkeyCombo? combo) => Update(v => _visibility = v, KeyVisibility, combo);
    public void SetClickThrough(HotkeyCombo? combo) => Update(v => _clickThrough = v, KeyClickThrough, combo);

    private void Update(Action<HotkeyCombo?> assign, string key, HotkeyCombo? value)
    {
        assign(value);
        _props.SetProperty(key, value?.ToString() ?? NoneSentinel);
        if (_running)
        {
            Stop();
            Start();
        }
    }

    private HotkeyCombo? Load(string key, HotkeyCombo fallback)
    {
        string? raw = _props.GetProperty(key);
        if (raw == null)
        {
            return fallback; // never set → default
        }

        if (raw == NoneSentinel)
        {
            return null; // explicitly unassigned → no hotkey (do NOT fall back to the default)
        }

        return HotkeyCombo.TryParse(raw) ?? fallback;
    }

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _listener = new Thread(ListenLoop) { IsBackground = true, Name = "HotkeyListener" };
        _listener.Start();
    }

    private void ListenLoop()
    {
        // Register only the assigned combos (a null combo = unassigned → no global hotkey; RegisterHotKey
        // is short-circuited past for nulls). Registration may also fail when a combo is already owned by
        // another app. Either way we DON'T tear the thread down: the message loop stays alive so a later
        // SetX (which does Stop()/Start() only while _running) can re-register — even from the all-unassigned
        // state. The loop just idles (PeekMessage + sleep) when nothing is registered.
        if (_reset is { } r)
        {
            RegisterHotKey(IntPtr.Zero, ResetId, (uint)r.Modifiers, (uint)r.VkCode);
        }

        if (_visibility is { } v)
        {
            RegisterHotKey(IntPtr.Zero, VisibilityId, (uint)v.Modifiers, (uint)v.VkCode);
        }

        if (_clickThrough is { } c)
        {
            RegisterHotKey(IntPtr.Zero, ClickThroughId, (uint)c.Modifiers, (uint)c.VkCode);
        }

        try
        {
            while (_running)
            {
                if (PeekMessageW(out MSG msg, IntPtr.Zero, 0, 0, PmRemove))
                {
                    if (msg.message == WmHotkey && PassesRepeatGuard((int)msg.wParam))
                    {
                        switch ((int)msg.wParam)
                        {
                            case ResetId:
                                OnReset?.Invoke();
                                break;
                            case VisibilityId:
                                OnVisibility?.Invoke();
                                break;
                            case ClickThroughId:
                                OnClickThrough?.Invoke();
                                break;
                        }
                    }
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
        }
        finally
        {
            UnregisterHotKey(IntPtr.Zero, ResetId);
            UnregisterHotKey(IntPtr.Zero, VisibilityId);
            UnregisterHotKey(IntPtr.Zero, ClickThroughId);
        }
    }

    /// <summary>Leading-edge debounce against a held hotkey's auto-repeat. Returns true the first time an id
    /// fires after a quiet gap, false while the same id keeps arriving within <see cref="HotkeyRepeatSuppressMs"/>.
    /// The timestamp is updated on EVERY message, so a continuous repeat stream keeps extending the quiet
    /// window and never re-fires. Runs only on the listener thread, so it needs no synchronization.</summary>
    private bool PassesRepeatGuard(int id)
    {
        long now = Environment.TickCount64;
        bool fire = !_lastHotkeyTick.TryGetValue(id, out long last) || now - last >= HotkeyRepeatSuppressMs;
        _lastHotkeyTick[id] = now;
        return fire;
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _listener?.Join(1000);
        _listener = null;
    }

    public void Dispose() => Stop();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }
}

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
/// Verbatim port of Kotlin <c>config.HotkeyHandler</c>: registers three global hotkeys (reset
/// combat / toggle visibility / toggle click-through) and pumps a message loop on a dedicated thread,
/// dispatching to callbacks. Combos persist via <see cref="PropertyHandler"/> (keys hotkey/hideHotkey/
/// clickThroughHotkey) with the same defaults (Ctrl+R / Ctrl+H / Ctrl+T). The WPF app wires the
/// callbacks to overlay actions.
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

    private const string KeyReset = "hotkey";
    private const string KeyVisibility = "hideHotkey";
    private const string KeyClickThrough = "clickThroughHotkey";

    private static readonly HotkeyCombo DefaultReset = new(ModControl, 0x52);        // Ctrl+R
    private static readonly HotkeyCombo DefaultVisibility = new(ModControl, 0x48);   // Ctrl+H
    private static readonly HotkeyCombo DefaultClickThrough = new(ModControl, 0x54); // Ctrl+T

    private readonly PropertyHandler _props;
    private volatile HotkeyCombo _reset;
    private volatile HotkeyCombo _visibility;
    private volatile HotkeyCombo _clickThrough;
    private Thread? _listener;
    private volatile bool _running;

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

    public HotkeyCombo Reset => _reset;
    public HotkeyCombo Visibility => _visibility;
    public HotkeyCombo ClickThrough => _clickThrough;

    public void SetReset(int modifiers, int vkCode) => Update(v => _reset = v, KeyReset, new HotkeyCombo(modifiers, vkCode));
    public void SetVisibility(int modifiers, int vkCode) => Update(v => _visibility = v, KeyVisibility, new HotkeyCombo(modifiers, vkCode));
    public void SetClickThrough(int modifiers, int vkCode) => Update(v => _clickThrough = v, KeyClickThrough, new HotkeyCombo(modifiers, vkCode));

    private void Update(Action<HotkeyCombo> assign, string key, HotkeyCombo value)
    {
        assign(value);
        _props.SetProperty(key, value.ToString());
        if (_running)
        {
            Stop();
            Start();
        }
    }

    private HotkeyCombo Load(string key, HotkeyCombo fallback)
    {
        string? raw = _props.GetProperty(key);
        return raw != null ? HotkeyCombo.TryParse(raw) ?? fallback : fallback;
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
        bool reset = RegisterHotKey(IntPtr.Zero, ResetId, (uint)_reset.Modifiers, (uint)_reset.VkCode);
        bool visibility = RegisterHotKey(IntPtr.Zero, VisibilityId, (uint)_visibility.Modifiers, (uint)_visibility.VkCode);
        bool clickThrough = RegisterHotKey(IntPtr.Zero, ClickThroughId, (uint)_clickThrough.Modifiers, (uint)_clickThrough.VkCode);
        if (!reset && !visibility && !clickThrough)
        {
            _running = false;
            return;
        }

        try
        {
            while (_running)
            {
                if (PeekMessageW(out MSG msg, IntPtr.Zero, 0, 0, PmRemove))
                {
                    if (msg.message == WmHotkey)
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

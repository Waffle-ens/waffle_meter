namespace WaffleMeter.App.Core;

/// <summary>
/// Port of the React utils/hotKey.ts label formatter so rebound hotkeys display identically
/// ("CTRL + R", "CTRL + ALT + F1", "NUMPAD 5"). Modifiers: MOD_ALT=1, MOD_CTRL=2.
/// </summary>
public static class HotkeyFormat
{
    public static string Format(int modifiers, int vkCode)
    {
        var parts = new List<string>();
        if ((modifiers & HotkeyHandler.ModControl) != 0)
        {
            parts.Add("CTRL");
        }

        if ((modifiers & HotkeyHandler.ModAlt) != 0)
        {
            parts.Add("ALT");
        }

        parts.Add(VkLabel(vkCode));
        return string.Join(" + ", parts);
    }

    public static string VkLabel(int vk) => vk switch
    {
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),       // 0-9
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),       // A-Z
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),           // F1-F24
        >= 0x60 and <= 0x69 => "NUMPAD " + (vk - 0x60),     // NUMPAD 0-9
        0x6A => "NUMPAD *",
        0x6B => "NUMPAD +",
        0x6D => "NUMPAD -",
        0x6E => "NUMPAD .",
        0x6F => "NUMPAD /",
        0x1B => "ESC",
        0x20 => "SPACE",
        0x0D => "ENTER",
        0x09 => "TAB",
        0x08 => "BACKSPACE",
        0x2E => "DELETE",
        0x2D => "INSERT",
        0x24 => "HOME",
        0x23 => "END",
        0x21 => "PAGE UP",
        0x22 => "PAGE DOWN",
        0x25 => "LEFT",
        0x26 => "UP",
        0x27 => "RIGHT",
        0x28 => "DOWN",
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        _ => "VK_" + vk,
    };
}

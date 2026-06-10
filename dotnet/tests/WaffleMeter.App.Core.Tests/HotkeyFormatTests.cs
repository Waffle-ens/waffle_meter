using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

public sealed class HotkeyFormatTests
{
    [Theory]
    [InlineData(HotkeyHandler.ModControl, 0x52, "CTRL + R")]
    [InlineData(HotkeyHandler.ModControl, 0x48, "CTRL + H")]
    [InlineData(HotkeyHandler.ModControl | HotkeyHandler.ModAlt, 0x70, "CTRL + ALT + F1")]
    [InlineData(HotkeyHandler.ModControl, 0x65, "CTRL + NUMPAD 5")]
    [InlineData(HotkeyHandler.ModControl, 0x1B, "CTRL + ESC")]
    public void Format_matches_react_labels(int modifiers, int vk, string expected)
    {
        Assert.Equal(expected, HotkeyFormat.Format(modifiers, vk));
    }

    [Theory]
    [InlineData(0x41, "A")]
    [InlineData(0x39, "9")]
    [InlineData(0x87, "F24")]
    [InlineData(0x6B, "NUMPAD +")]
    [InlineData(0x25, "LEFT")]
    [InlineData(0x999, "VK_2457")]
    public void VkLabel_covers_ranges(int vk, string expected)
    {
        Assert.Equal(expected, HotkeyFormat.VkLabel(vk));
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WaffleMeter.App.Core;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Interactive hotkey rebinding box (React useHotkeyCapture): focus to capture; requires Ctrl and/or
/// Alt (rejects Shift/Win and pure modifier keys); maps the WPF key to a Win32 VK so the stored combo
/// matches the JS keyCode. Two-way <see cref="Combo"/> binds to the view model's pending hotkey.
/// </summary>
public sealed class HotkeyCaptureBox : TextBox
{
    private static readonly int[] PureModifiers = { 0x10, 0x11, 0x12, 0x5B, 0x5C };

    public static readonly DependencyProperty ComboProperty = DependencyProperty.Register(
        nameof(Combo),
        typeof(HotkeyCombo),
        typeof(HotkeyCaptureBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnComboChanged));

    public HotkeyCaptureBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        GotKeyboardFocus += (_, _) => Text = "키 입력…";
        LostKeyboardFocus += (_, _) => UpdateText();
    }

    public HotkeyCombo? Combo
    {
        get => (HotkeyCombo?)GetValue(ComboProperty);
        set => SetValue(ComboProperty, value);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        ModifierKeys mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Shift) || mods.HasFlag(ModifierKeys.Windows))
        {
            return;
        }

        if (!mods.HasFlag(ModifierKeys.Control) && !mods.HasFlag(ModifierKeys.Alt))
        {
            return; // a Ctrl/Alt modifier is required
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (Array.IndexOf(PureModifiers, vk) >= 0)
        {
            return; // ignore pure modifier presses
        }

        int modifiers = (mods.HasFlag(ModifierKeys.Control) ? HotkeyHandler.ModControl : 0)
                        | (mods.HasFlag(ModifierKeys.Alt) ? HotkeyHandler.ModAlt : 0);
        Combo = new HotkeyCombo(modifiers, vk);
    }

    private static void OnComboChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HotkeyCaptureBox)d).UpdateText();

    private void UpdateText() => Text = Combo != null ? HotkeyFormat.Format(Combo.Modifiers, Combo.VkCode) : string.Empty;
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// View model for the alarm toast (슈고 페스타 and, later, user custom alarms). UI-thread only. The glyph is a
/// Segoe MDL2 Assets code point (a ringer/bell), built from a char code to avoid a literal PUA char.
/// </summary>
public sealed class AlarmToastViewModel : INotifyPropertyChanged
{
    private static readonly Brush Amber = Frozen(Color.FromRgb(0xFB, 0xBF, 0x24));
    private static readonly string GlyphBell = ((char)0xEA8F).ToString(); // Ringer

    private string _title = string.Empty;
    public string Title { get => _title; private set => Set(ref _title, value); }

    /// <summary>The alert read aloud by TTS — the title plus its one-line detail.</summary>
    public string SpokenText => $"{Title}. {Description}";

    private string _description = string.Empty;
    public string Description { get => _description; private set => Set(ref _description, value); }

    private string _iconGlyph = GlyphBell;
    public string IconGlyph { get => _iconGlyph; private set => Set(ref _iconGlyph, value); }

    private Brush _iconBrush = Amber;
    public Brush IconBrush { get => _iconBrush; private set => Set(ref _iconBrush, value); }

    /// <summary>Set the toast for a 슈고 페스타 cue. <paramref name="lead"/> 0 = 시작, else N분 전.</summary>
    public void SetShugo(int lead)
    {
        Title = "슈고 페스타";
        Description = lead <= 0 ? "지금 시작합니다!" : $"{lead}분 뒤 시작합니다.";
        IconGlyph = GlyphBell;
        IconBrush = Amber;
    }

    /// <summary>Set the toast for a user custom alarm.</summary>
    public void SetCustom(string title)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "알람" : title;
        Description = "지금입니다.";
        IconGlyph = GlyphBell;
        IconBrush = Amber;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

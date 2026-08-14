using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// One card in the settings font picker. The card IS the preview: its title is drawn in the font it offers,
/// so the grid answers "what does this look like" without the user selecting anything and staring at the meter.
/// <para><see cref="Value"/> is the string that lands in <c>settings.properties</c> and that
/// <see cref="FontResolver.Resolve"/> matches on — a font's INTERNAL family name, sometimes its typographic
/// family (<c>Freesentation</c>, <c>Tmoney RoundWind</c>) and sometimes family+face (<c>Pretendard Bold</c>).
/// It is frozen: tidying it up silently breaks every existing user's saved setting, which then falls back to
/// 맑은 고딕 with no error. Change <see cref="Title"/> freely, never <see cref="Value"/>.</para>
/// </summary>
public sealed class FontCardViewModel : INotifyPropertyChanged
{
    /// <summary>Digits and Hangul together — the meter shows both, and faces differ far more in digits than
    /// people expect (a 1 that reads as a 7 is a real complaint).</summary>
    public const string Sample = "와플미터 1,234";

    /// <summary>Latin-only faces get a sample they can actually draw, plus a badge saying so.</summary>
    private const string LatinSample = "Waffle 1,234";

    private bool _isSelected;

    public FontCardViewModel(string title, string value, bool isDefault)
    {
        Title = title;
        Value = value;
        IsDefault = isDefault;
        Preview = FontResolver.Resolve(value);
        Origin = FontResolver.Classify(value);
        NoHangul = !GlyphFallback.CanRender(value, "가");
        SampleText = NoHangul ? LatinSample : Sample;
    }

    /// <summary>User-facing name. Free to change — it is not the stored value.</summary>
    public string Title { get; }

    /// <summary>The stored setting value. Frozen; see the type remarks.</summary>
    public string Value { get; }

    public bool IsDefault { get; }

    public FontResolver.FontOrigin Origin { get; }

    /// <summary>Resolved once at construction. Binding the converter in the template instead would re-probe
    /// pack URIs for every card each time the skin or selection changes.</summary>
    public FontFamily Preview { get; }

    public string SampleText { get; }

    /// <summary>This face has no Hangul — the card says so rather than previewing tofu.</summary>
    public bool NoHangul { get; }

    /// <summary>Badge text; empty when the card needs none (a plain bundled font).</summary>
    public string Badge => Origin switch
    {
        FontResolver.FontOrigin.User => "사용자",
        FontResolver.FontOrigin.System => "시스템",
        _ => IsDefault ? "기본" : string.Empty,
    };

    public bool HasBadge => Badge.Length > 0;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

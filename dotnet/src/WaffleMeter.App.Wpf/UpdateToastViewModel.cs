using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// View model for the update toast (port of React UpdateToast). One toast updates in place across
/// stages: downloading (progress bar), ready (restart button), failed (message). UI-thread only.
/// Glyphs are Segoe MDL2 Assets code points (built from char codes to avoid literal PUA chars).
/// </summary>
public sealed class UpdateToastViewModel : INotifyPropertyChanged
{
    private static readonly Brush Cyan = Frozen(Color.FromRgb(0x2D, 0xD4, 0xBF));
    private static readonly Brush Emerald = Frozen(Color.FromRgb(0x34, 0xD3, 0x99));
    private static readonly Brush Rose = Frozen(Color.FromRgb(0xFB, 0x71, 0x85));

    private static readonly string GlyphDownload = ((char)0xE896).ToString();
    private static readonly string GlyphReady = ((char)0xEC61).ToString();    // CompletedSolid
    private static readonly string GlyphError = ((char)0xE783).ToString();    // ErrorBadge

    private string _title = string.Empty;
    public string Title { get => _title; private set => Set(ref _title, value); }

    private string _description = string.Empty;
    public string Description { get => _description; private set => Set(ref _description, value); }

    private string _iconGlyph = ((char)0xE896).ToString();
    public string IconGlyph { get => _iconGlyph; private set => Set(ref _iconGlyph, value); }

    private Brush _iconBrush = Cyan;
    public Brush IconBrush { get => _iconBrush; private set => Set(ref _iconBrush, value); }

    private Visibility _progressVisibility = Visibility.Collapsed;
    public Visibility ProgressVisibility { get => _progressVisibility; private set => Set(ref _progressVisibility, value); }

    private double _progressRatio;
    public double ProgressRatio { get => _progressRatio; private set => Set(ref _progressRatio, value); }

    private double _progressRest = 1.0;
    public double ProgressRest { get => _progressRest; private set => Set(ref _progressRest, value); }

    private string _percentText = "0%";
    public string PercentText { get => _percentText; private set => Set(ref _percentText, value); }

    private Visibility _restartVisibility = Visibility.Collapsed;
    public Visibility RestartVisibility { get => _restartVisibility; private set => Set(ref _restartVisibility, value); }

    public void SetDownloading(string version, int percent)
    {
        Title = $"v{version} 업데이트";
        Description = "다운로드 중";
        IconGlyph = GlyphDownload;
        IconBrush = Cyan;
        double r = Math.Clamp(percent / 100.0, 0, 1);
        ProgressRatio = r;
        ProgressRest = 1.0 - r;
        PercentText = $"{percent}%";
        ProgressVisibility = Visibility.Visible;
        RestartVisibility = Visibility.Collapsed;
    }

    public void SetReady(string version)
    {
        Title = "다운로드 완료";
        Description = $"v{version} — 재시작 시 적용됩니다.";
        IconGlyph = GlyphReady;
        IconBrush = Emerald;
        ProgressVisibility = Visibility.Collapsed;
        RestartVisibility = Visibility.Visible;
    }

    public void SetFailed(string message)
    {
        Title = "업데이트 실패";
        Description = "네트워크 또는 권한 문제를 확인해 주세요.";
        IconGlyph = GlyphError;
        IconBrush = Rose;
        ProgressVisibility = Visibility.Collapsed;
        RestartVisibility = Visibility.Collapsed;
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

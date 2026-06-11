using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaffleMeter.App.Core;
using WaffleMeter.Data;

namespace WaffleMeter.App.Wpf;

/// <summary>One skill badge on a join card: icon + "name LvN" + job-tinted pill colors.</summary>
public sealed record JoinSkillBadge(System.Windows.Media.ImageSource? Icon, string Label, Brush Background, Brush BorderBrush);

/// <summary>
/// View model for the party join-request panel (port of React JoinRequestPanel). <see cref="Reconcile"/>
/// (on store Changed) rebuilds the row list newest-first; <see cref="Tick"/> (a 250ms UI timer) drives
/// each row's 20s countdown bar and evicts a row when it reaches 0 (web per-row setTimeout parity).
/// All methods run on the UI thread.
/// </summary>
public sealed class JoinRequestViewModel : INotifyPropertyChanged
{
    public JoinRequestViewModel(MeterSettings settings, IEnumerable<int>? visibleCodes = null)
    {
        Settings = settings;
        _visibleCodes = visibleCodes != null ? new HashSet<int>(visibleCodes) : new HashSet<int>(SkillCatalog.DefaultVisibleCodes);
    }

    /// <summary>Exposed so the panel can bind the user's overlay font.</summary>
    public MeterSettings Settings { get; }

    private HashSet<int> _visibleCodes;

    /// <summary>Replace the visible-skill set (skill-settings flyout). Clears rows so the next reconcile
    /// rebuilds each card's badges against the new set.</summary>
    public void SetVisibleCodes(IEnumerable<int> codes)
    {
        _visibleCodes = new HashSet<int>(codes);
        Rows.Clear();
    }

    public ObservableCollection<JoinRequestRowViewModel> Rows { get; } = new();

    /// <summary>Raised when the list goes from empty to non-empty (web auto-open on 0→≥1).</summary>
    public event Action? RequestPresent;

    private int _lastCount;

    private string _countText = "0건";
    public string CountText { get => _countText; private set => Set(ref _countText, value); }

    private Visibility _emptyVisibility = Visibility.Visible;
    public Visibility EmptyVisibility { get => _emptyVisibility; private set => Set(ref _emptyVisibility, value); }

    /// <summary>Reconcile the rows against a newest-first snapshot, preserving existing row objects so
    /// in-flight countdown bars stay smooth.</summary>
    public void Reconcile(IReadOnlyList<JoinRequestUser> snapshot)
    {
        // Drop rows no longer present.
        var present = new HashSet<int>(snapshot.Select(s => s.Requester));
        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (!present.Contains(Rows[i].Id))
            {
                Rows.RemoveAt(i);
            }
        }

        // Insert/reorder to match the snapshot order (newest first).
        for (int i = 0; i < snapshot.Count; i++)
        {
            JoinRequestUser u = snapshot[i];
            int existing = IndexOfId(u.Requester);
            if (existing < 0)
            {
                Rows.Insert(i, new JoinRequestRowViewModel(u, _visibleCodes));
            }
            else if (existing != i)
            {
                Rows.Move(existing, i);
            }
        }

        UpdateCount();
        if (_lastCount == 0 && Rows.Count > 0)
        {
            RequestPresent?.Invoke();
        }

        _lastCount = Rows.Count;
        Tick(); // paint timers for any newly inserted rows immediately
    }

    /// <summary>Clear all rows (instance start / party exit).</summary>
    public void Clear()
    {
        Rows.Clear();
        _lastCount = 0;
        UpdateCount();
    }

    /// <summary>250ms heartbeat: refresh each row's bar; evict rows past 20s.</summary>
    public void Tick()
    {
        if (Rows.Count == 0)
        {
            return;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long latest = Rows.Max(r => r.ArrivedAt);
        long effectiveNow = Math.Max(now, latest); // web effectiveNow guard (no negative bars)

        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (Rows[i].UpdateTimer(effectiveNow) <= 0)
            {
                Rows.RemoveAt(i);
            }
        }

        if (Rows.Count != _lastCount)
        {
            _lastCount = Rows.Count;
            UpdateCount();
        }
    }

    private void UpdateCount()
    {
        CountText = $"{Rows.Count}건";
        EmptyVisibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private int IndexOfId(int id)
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].Id == id)
            {
                return i;
            }
        }

        return -1;
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

/// <summary>
/// One join-request card. Static fields (name/power/icon/job colors) are set from the packet; the
/// countdown fields (<see cref="BarRatio"/>, <see cref="FillBrush"/>, <see cref="RemainingText"/>) are
/// recomputed on each <see cref="UpdateTimer"/> tick.
/// </summary>
public sealed class JoinRequestRowViewModel : INotifyPropertyChanged
{
    private const double TotalSec = 20.0;

    // Timer-bar fills by remaining fraction (web gradients), frozen + shared.
    private static readonly Brush TealFill = Gradient("#FF2DD4BF", "#FF0F766E");
    private static readonly Brush AmberFill = Gradient("#FFF59E0B", "#FFB45309");
    private static readonly Brush RoseFill = Gradient("#FFFB7185", "#FFBE123C");

    public JoinRequestRowViewModel(JoinRequestUser u, ISet<int> visibleCodes)
    {
        Id = u.Requester;
        ArrivedAt = u.ArrivedAt;

        string label = ServerNames.GetServerLabel(u.Server);
        Nickname = label.Length > 0 ? $"{u.Nickname}[{label}]" : u.Nickname;

        PowerText = u.Power > 0 ? MeterFormat.FormatPower(u.Power) : "-";
        JobIcon = JoinIcons.Job(u.Job);

        JobColors colors = JoinPanelPalette.For(u.Job);
        CardBrush = colors.Card;
        BorderBrush = colors.Border;
        AccentBrush = colors.Accent;

        BuildBadges(u.Skill, visibleCodes, colors, out List<JoinSkillBadge> normal, out List<JoinSkillBadge> stigma);
        NormalBadges = normal;
        StigmaBadges = stigma;
        NormalBadgesVisibility = normal.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        StigmaBadgesVisibility = stigma.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public int Id { get; }
    public long ArrivedAt { get; }
    public string Nickname { get; }
    public string PowerText { get; }
    public BitmapImage? JobIcon { get; }
    public Brush CardBrush { get; }
    public Brush BorderBrush { get; }
    public Brush AccentBrush { get; }
    public IReadOnlyList<JoinSkillBadge> NormalBadges { get; }
    public IReadOnlyList<JoinSkillBadge> StigmaBadges { get; }
    public Visibility NormalBadgesVisibility { get; }
    public Visibility StigmaBadgesVisibility { get; }

    // From the requester's skills: normalize → keep only visible → dedupe (max lv) → sort by catalog
    // order → split 일반/스티그마 (port of JoinRequestPanel badgeMap + SkillBadges).
    private static void BuildBadges(IReadOnlyDictionary<int, int> skill, ISet<int> visibleCodes, JobColors colors,
        out List<JoinSkillBadge> normal, out List<JoinSkillBadge> stigma)
    {
        normal = new List<JoinSkillBadge>();
        stigma = new List<JoinSkillBadge>();
        if (skill.Count == 0)
        {
            return;
        }

        var merged = new Dictionary<int, (string Name, int Lv, bool Stigma)>();
        foreach ((int rawCode, int lv) in skill)
        {
            int code = SkillCatalog.Normalize(rawCode);
            if (!visibleCodes.Contains(code))
            {
                continue;
            }

            SkillMeta? meta = SkillCatalog.Get(code);
            string name = meta?.Name ?? rawCode.ToString();
            bool isStigma = meta?.IsStigma ?? false;
            int level = merged.TryGetValue(code, out var prev) ? Math.Max(prev.Lv, lv) : lv;
            merged[code] = (name, level, isStigma);
        }

        foreach ((int code, var b) in merged.OrderBy(kv => SkillCatalog.Order(kv.Key)))
        {
            var badge = new JoinSkillBadge(JoinIcons.Skill(code), $"{b.Name} Lv{b.Lv}", colors.BadgeBg, colors.Border);
            (b.Stigma ? stigma : normal).Add(badge);
        }
    }

    private double _barRatio = 1.0;
    public double BarRatio { get => _barRatio; private set => Set(ref _barRatio, value); }

    private double _barRest;
    public double BarRest { get => _barRest; private set => Set(ref _barRest, value); }

    private Brush _fillBrush = TealFill;
    public Brush FillBrush { get => _fillBrush; private set => Set(ref _fillBrush, value); }

    private string _remainingText = "20s";
    public string RemainingText { get => _remainingText; private set => Set(ref _remainingText, value); }

    /// <summary>Recompute the bar from the shared effective-now; returns the remaining seconds.</summary>
    public double UpdateTimer(long effectiveNowMs)
    {
        double remaining = Math.Max(0, TotalSec - (effectiveNowMs - ArrivedAt) / 1000.0);
        double pct = remaining / TotalSec;
        BarRatio = pct;
        BarRest = 1.0 - pct;
        FillBrush = pct > 0.5 ? TealFill : pct > 0.25 ? AmberFill : RoseFill;
        RemainingText = $"{Math.Ceiling(remaining):0}s";
        return remaining;
    }

    private static Brush Gradient(string from, string to)
    {
        var brush = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(from),
            (Color)ColorConverter.ConvertFromString(to),
            0.0); // left -> right
        brush.Freeze();
        return brush;
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

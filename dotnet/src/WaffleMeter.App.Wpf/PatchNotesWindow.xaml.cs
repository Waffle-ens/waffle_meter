using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// One-time post-update patch-note popup. Renders a single version's RELEASE_NOTES section (<c>## [태그] 제목</c>
/// sub-headings + <c>"- "</c> bullets, from <see cref="App.Core.PatchNotesProvider"/>) into a scrollable, skinned
/// list. Purely informational: closing dismisses it, and the caller has already recorded the version so it never
/// re-pops. Never throws into startup (the App guards the show call).
/// </summary>
public partial class PatchNotesWindow : Window
{
    /// <summary>Chip colours per skin family. The dark set is tuned for #070C14; the light set needs real
    /// contrast against #FAFCFF, where the dark set's pale mint text is effectively invisible.</summary>
    private readonly bool _isLight;

    public PatchNotesWindow(string version, string notesMarkdown, bool isLight = false)
    {
        InitializeComponent();
        _isLight = isLight;
        TitleText.Text = $"v{version} 업데이트됨";
        Render(notesMarkdown);
    }

    /// <summary>Tag → chip colour. The tag is the first thing a reader scans for ("뭐가 고쳐졌지?"), so it is
    /// lifted out of the heading text into a coloured chip instead of sitting inline as literal brackets.</summary>
    private static readonly Dictionary<string, (Brush Fill, Brush Text)> DarkChips = new(StringComparer.Ordinal)
    {
        ["추가"] = (Frozen(0x1F, 0x2D, 0xD4, 0xBF), Frozen(0xFF, 0x5E, 0xEA, 0xD4)),
        ["수정"] = (Frozen(0x24, 0xFB, 0xBF, 0x24), Frozen(0xFF, 0xFC, 0xD3, 0x4D)),
        ["변경"] = (Frozen(0x24, 0x60, 0xA5, 0xFA), Frozen(0xFF, 0x93, 0xC5, 0xFD)),
    };

    private static readonly Dictionary<string, (Brush Fill, Brush Text)> LightChips = new(StringComparer.Ordinal)
    {
        ["추가"] = (Frozen(0x1F, 0x0F, 0x76, 0x6E), Frozen(0xFF, 0x0F, 0x76, 0x6E)),
        ["수정"] = (Frozen(0x24, 0xB4, 0x53, 0x09), Frozen(0xFF, 0xB4, 0x53, 0x09)),
        ["변경"] = (Frozen(0x24, 0x1D, 0x4E, 0xD8), Frozen(0xFF, 0x1D, 0x4E, 0xD8)),
    };

    private void Render(string notes)
    {
        bool first = true;
        foreach (string raw in notes.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            // Indentation carries meaning (a nested bullet is a detail of the one above), so it must be read
            // BEFORE trimming — the previous version trimmed first and rendered nested bullets as bare prose.
            int indent = raw.Length - raw.TrimStart().Length;
            string line = Clean(raw.Trim());
            if (line.Length == 0)
            {
                NotesPanel.Children.Add(new Border { Height = 6 });
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                NotesPanel.Children.Add(BuildHeading(line[3..].Trim(), first));
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                NotesPanel.Children.Add(BuildBullet(line[2..].Trim(), indent >= 2));
            }
            else
            {
                var prose = new TextBlock
                {
                    Text = line, FontSize = 12.5, LineHeight = 18,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2),
                };
                prose.SetResourceReference(TextBlock.ForegroundProperty, "Skin.Fg");
                NotesPanel.Children.Add(prose);
            }

            first = false;
        }
    }

    /// <summary>"[추가] 던전 티어" → a coloured 추가 chip followed by the plain title. An unknown or missing tag
    /// falls back to the whole line as the title, so a note that skips the convention still renders.</summary>
    private UIElement BuildHeading(string heading, bool first)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, first ? 0 : 14, 0, 5),
        };

        string title = heading;
        if (heading.StartsWith('[') && heading.IndexOf(']') is int close and > 1)
        {
            string tag = heading[1..close].Trim();
            if ((_isLight ? LightChips : DarkChips).TryGetValue(tag, out (Brush Fill, Brush Text) chip))
            {
                panel.Children.Add(new Border
                {
                    Background = chip.Fill,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(0, 1, 8, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock
                    {
                        Text = tag, Foreground = chip.Text, FontSize = 11, FontWeight = FontWeights.Bold,
                    },
                });
                title = heading[(close + 1)..].Trim();
            }
        }

        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 340,
        };
        // Skin.Accent, not a hardcoded mint: the light palette's accent is #0F766E, and the dark one washes
        // out to near-invisible on #FAFCFF.
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "Skin.Accent");
        panel.Children.Add(titleText);
        return panel;
    }

    private static UIElement BuildBullet(string text, bool nested)
    {
        var row = new Grid { Margin = new Thickness(nested ? 16 : 2, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dot = new TextBlock
        {
            Text = nested ? "·" : "•",
            FontSize = nested ? 14 : 12.5,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        dot.SetResourceReference(TextBlock.ForegroundProperty, nested ? "Skin.MutedFg" : "Skin.Accent");
        var body = new TextBlock
        {
            Text = text, FontSize = nested ? 12 : 12.5,
            LineHeight = 18, TextWrapping = TextWrapping.Wrap,
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, nested ? "Skin.MutedFg" : "Skin.Fg");
        Grid.SetColumn(dot, 0);
        Grid.SetColumn(body, 1);
        row.Children.Add(dot);
        row.Children.Add(body);
        return row;
    }

    // Strip markdown emphasis markers so the plain text reads cleanly in the styled list.
    private static string Clean(string s) => s.Replace("**", "").Replace("`", "");

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Brush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

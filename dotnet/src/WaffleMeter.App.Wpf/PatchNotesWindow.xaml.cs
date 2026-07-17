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
    private static readonly Brush Accent = Frozen(0x7D, 0xE8, 0xDD);

    public PatchNotesWindow(string version, string notesMarkdown)
    {
        InitializeComponent();
        TitleText.Text = $"v{version} 업데이트됨";
        Render(notesMarkdown);
    }

    private void Render(string notes)
    {
        Brush fg = TryFindResource("Skin.Fg") as Brush ?? Brushes.Gainsboro;
        bool first = true;
        foreach (string raw in notes.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = Clean(raw.Trim());
            if (line.Length == 0)
            {
                NotesPanel.Children.Add(new Border { Height = 6 });
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                NotesPanel.Children.Add(new TextBlock
                {
                    Text = line[3..].Trim(),
                    Foreground = Accent,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, first ? 0 : 12, 0, 4),
                });
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                var row = new Grid { Margin = new Thickness(2, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var dot = new TextBlock
                {
                    Text = "•", Foreground = Accent, FontSize = 12.5,
                    Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top,
                };
                var body = new TextBlock
                {
                    Text = line[2..].Trim(), Foreground = fg, FontSize = 12.5,
                    LineHeight = 18, TextWrapping = TextWrapping.Wrap,
                };
                Grid.SetColumn(dot, 0);
                Grid.SetColumn(body, 1);
                row.Children.Add(dot);
                row.Children.Add(body);
                NotesPanel.Children.Add(row);
            }
            else
            {
                NotesPanel.Children.Add(new TextBlock
                {
                    Text = line, Foreground = fg, FontSize = 12.5, LineHeight = 18,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2),
                });
            }

            first = false;
        }
    }

    // Strip markdown emphasis markers so the plain text reads cleanly in the styled list.
    private static string Clean(string s) => s.Replace("**", "").Replace("`", "");

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

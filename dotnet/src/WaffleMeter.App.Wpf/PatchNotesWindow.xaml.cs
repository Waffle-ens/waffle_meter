using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WaffleMeter.App.Core;

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
        var table = new List<string[]>();
        foreach (string raw in notes.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            // Indentation carries meaning (a nested bullet is a detail of the one above), so it must be read
            // BEFORE trimming — the previous version trimmed first and rendered nested bullets as bare prose.
            int indent = raw.Length - raw.TrimStart().Length;
            string line = Clean(raw.Trim());

            // A run of "| a | b |" lines is one table. Buffered rather than rendered per line, because a Grid
            // needs its column and row count up front.
            if (line.StartsWith('|'))
            {
                string[] cells = SplitRow(line);
                if (!IsRule(cells))
                {
                    table.Add(cells);
                }

                continue;
            }

            FlushTable(table);
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

        FlushTable(table); // a table that ends the section has no following line to flush it
    }

    private static string[] SplitRow(string line) =>
        line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();

    /// <summary>The "|---|---|" rule under a header carries no content — it exists so the same source renders
    /// as a table on GitHub, where these notes are also read.</summary>
    private static bool IsRule(string[] cells) =>
        cells.Length > 0 && cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':'));

    private void FlushTable(List<string[]> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        NotesPanel.Children.Add(BuildTable(rows));
        rows.Clear();
    }

    /// <summary>First row is the header. Column 0 hugs its content and the rest share the remainder, which fits
    /// every table these notes carry (a short label plus prose).</summary>
    private UIElement BuildTable(List<string[]> rows)
    {
        int columns = rows.Max(r => r.Length);
        var grid = new Grid { Margin = new Thickness(2, 4, 0, 6) };
        for (int c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = c == 0 ? GridLength.Auto : new GridLength(1, GridUnitType.Star),
            });
        }

        for (int r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool header = r == 0;
            for (int c = 0; c < columns; c++)
            {
                string text = c < rows[r].Length ? rows[r][c] : string.Empty;
                FrameworkElement cell = !header && c == 0 && TierRankOf(text) is int rank and > 0
                    ? TierChip(text, rank)
                    : TextCell(text, header, c);
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }

            if (header)
            {
                var underline = new Border
                {
                    Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2),
                };
                underline.SetResourceReference(Border.BackgroundProperty, "Skin.SoftBorder");
                Grid.SetRow(underline, 0);
                Grid.SetColumnSpan(underline, columns);
                grid.Children.Add(underline);
            }
        }

        var frame = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 2, 0, 4),
            Child = grid,
        };
        frame.SetResourceReference(Border.BackgroundProperty, "Skin.StatBg");
        frame.SetResourceReference(Border.BorderBrushProperty, "Skin.SoftBorder");
        return frame;
    }

    private static FrameworkElement TextCell(string text, bool header, int column)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = header ? 11.5 : 12,
            FontWeight = header ? FontWeights.Bold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(column == 0 ? 0 : 12, 3, 0, 3),
            VerticalAlignment = VerticalAlignment.Center,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty,
            header ? "Skin.Accent" : column == 0 ? "Skin.Fg" : "Skin.MutedFg");
        return block;
    }

    /// <summary>A tier name in the first column renders as the badge the meter actually draws, so the table
    /// teaches the colour mapping instead of just listing names.</summary>
    private FrameworkElement TierChip(string name, int rank)
    {
        TierBadge badge = TierPalette.For(rank, _isLight);
        return new Border
        {
            Background = badge.ChipBg,
            BorderBrush = badge.RankRing,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 1, 7, 1),
            Margin = new Thickness(0, 3, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = name, Foreground = badge.ChipFg, FontSize = 11.5, FontWeight = FontWeights.Bold,
            },
        };
    }

    private static int TierRankOf(string name) => Array.IndexOf(TierLadder.TierNames, name) + 1;

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

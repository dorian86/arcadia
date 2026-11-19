using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Arcadia.Controls;

/// <summary>
/// Shows release notes written in light Markdown inside a StackPanel: "#" / "##" titles, "-" lists (indented
/// sub-lists), **bold**, `code`. Everything else is plain text. Used by the update / "Quoi de neuf" window.
/// </summary>
public static partial class MarkdownLite
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(MarkdownLite), new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Panel panel) return;
        panel.Children.Clear();
        foreach (var block in Parse(e.NewValue as string ?? ""))
            panel.Children.Add(Render(panel, block));
    }

    internal enum Kind { Title, Subtitle, Bullet, Paragraph }

    internal sealed record Block(Kind Kind, string Text, int Level = 0);

    internal static List<Block> Parse(string markdown)
    {
        var blocks = new List<Block>();
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var indent = raw.Length - raw.TrimStart().Length;
            var line = raw.Trim();
            if (line.StartsWith("# ")) blocks.Add(new Block(Kind.Title, line[2..].Trim()));
            else if (line.StartsWith("##")) blocks.Add(new Block(Kind.Subtitle, line.TrimStart('#').Trim()));
            else if (line.StartsWith("- ") || line.StartsWith("* ")) blocks.Add(new Block(Kind.Bullet, line[2..].Trim(), indent >= 2 ? 1 : 0));
            else if (blocks.Count > 0 && blocks[^1].Kind is Kind.Bullet or Kind.Paragraph && indent >= 2)
                blocks[^1] = blocks[^1] with { Text = blocks[^1].Text + " " + line }; // continuation of the previous line
            else blocks.Add(new Block(Kind.Paragraph, line));
        }
        return blocks;
    }

    private static UIElement Render(FrameworkElement owner, Block block)
    {
        Brush Res(string key, Brush fallback) => owner.TryFindResource(key) as Brush ?? fallback;
        var primary = Res("TextPrimary", Brushes.White);
        var secondary = Res("TextSecondary", Brushes.LightGray);
        var accent = Res("Accent", Brushes.Orange);

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20, Foreground = secondary };
        AddInlines(text.Inlines, block.Text, primary);
        switch (block.Kind)
        {
            case Kind.Title:
                text.FontSize = 16;
                text.FontWeight = FontWeights.SemiBold;
                text.Foreground = primary;
                text.Margin = new Thickness(0, 14, 0, 4);
                return text;
            case Kind.Subtitle:
                text.FontSize = 14;
                text.FontWeight = FontWeights.SemiBold;
                text.Foreground = primary;
                text.Margin = new Thickness(0, 10, 0, 4);
                return text;
            case Kind.Bullet:
                var row = new DockPanel { Margin = new Thickness(block.Level * 18, 3, 0, 3) };
                var dot = new TextBlock
                {
                    Text = block.Level == 0 ? "•" : "◦",
                    Foreground = accent,
                    FontSize = 13,
                    Width = 16,
                    LineHeight = 20,
                };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                row.Children.Add(text);
                return row;
            default:
                text.Margin = new Thickness(0, 3, 0, 3);
                return text;
        }
    }

    private static void AddInlines(InlineCollection inlines, string text, Brush strong)
    {
        var at = 0;
        foreach (Match m in Inline().Matches(text))
        {
            if (m.Index > at) inlines.Add(new Run(text[at..m.Index]));
            if (m.Groups["b"].Success) inlines.Add(new Run(m.Groups["b"].Value) { FontWeight = FontWeights.SemiBold, Foreground = strong });
            else inlines.Add(new Run(m.Groups["c"].Value) { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12 });
            at = m.Index + m.Length;
        }
        if (at < text.Length) inlines.Add(new Run(text[at..]));
    }

    [GeneratedRegex(@"\*\*(?<b>.+?)\*\*|`(?<c>[^`]+)`")]
    private static partial Regex Inline();
}

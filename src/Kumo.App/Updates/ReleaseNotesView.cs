using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Kumo.Core.Updates;

namespace Kumo.App.Updates;

/// <summary>Turns parsed release notes into native text elements that follow the window's Fluent theme.</summary>
internal static class ReleaseNotesView
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public static FrameworkElement Build(string? markdown)
    {
        var panel = new StackPanel();
        var blocks = ReleaseNotes.Parse(markdown);
        if (blocks.Count == 0)
        {
            panel.Children.Add(Secondary(new TextBlock { Text = "No release notes were published for this version.", TextWrapping = TextWrapping.Wrap }));
            return panel;
        }

        foreach (var block in blocks)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20 };
            AddInlines(text.Inlines, block.Inlines);
            switch (block.Kind)
            {
                case NotesBlockKind.Heading:
                    text.FontSize = block.Level == 1 ? 18 : block.Level == 2 ? 15 : 14;
                    text.FontWeight = FontWeights.SemiBold;
                    text.Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 14, 0, 6);
                    panel.Children.Add(text);
                    break;
                case NotesBlockKind.Bullet or NotesBlockKind.Numbered:
                    string marker = block.Kind == NotesBlockKind.Bullet ? "•" : $"{block.Level}.";
                    var row = new DockPanel { Margin = new Thickness(4, 2, 0, 2) };
                    var bullet = new TextBlock { Text = marker, Width = 18, FontSize = 13, LineHeight = 20 };
                    DockPanel.SetDock(bullet, Dock.Left);
                    row.Children.Add(Secondary(bullet));
                    row.Children.Add(text);
                    panel.Children.Add(row);
                    break;
                default:
                    text.Margin = new Thickness(0, 4, 0, 4);
                    panel.Children.Add(text);
                    break;
            }
        }
        return panel;
    }

    private static void AddInlines(InlineCollection target, IReadOnlyList<NotesInline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline.Kind)
            {
                case NotesInlineKind.Bold:
                    target.Add(new Bold(new Run(inline.Text)));
                    break;
                case NotesInlineKind.Code:
                    var code = new Run(inline.Text) { FontFamily = Mono, FontSize = 12 };
                    code.SetResourceReference(TextElement.BackgroundProperty, "SubtleFillColorSecondaryBrush");
                    target.Add(code);
                    break;
                case NotesInlineKind.Link when inline.Url is { } url:
                    var link = new Hyperlink(new Run(inline.Text)) { NavigateUri = url, ToolTip = url.AbsoluteUri };
                    link.RequestNavigate += (_, e) =>
                    {
                        if (e.Uri.Scheme == Uri.UriSchemeHttps)
                            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                        e.Handled = true;
                    };
                    target.Add(link);
                    break;
                default:
                    target.Add(new Run(inline.Text));
                    break;
            }
        }
    }

    private static TextBlock Secondary(TextBlock text)
    {
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return text;
    }
}

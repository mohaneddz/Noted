using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Noted.Markdown;

namespace Noted.Rendering;

/// <summary>
/// Folds an HTML <c>&lt;details&gt;…&lt;/details&gt;</c> block into a single "▸ summary" disclosure chip.
/// The interior lines are hidden by <see cref="BlockCollapser"/> (via <see cref="ICollapsibleBlockSource"/>);
/// clicking the chip — or moving the caret into the block — brings the raw HTML back so it can be edited.
/// </summary>
public sealed class DetailsElementGenerator : VisualLineElementGenerator, ICollapsibleBlockSource
{
    private readonly MarkdownAnalyzer _analyzer;
    private readonly RevealTracker _reveal;

    public DetailsElementGenerator(MarkdownAnalyzer analyzer, RevealTracker reveal)
    {
        _analyzer = analyzer;
        _reveal = reveal;
    }

    public bool HideMarkers { get; set; } = true;

    public EditorTheme Theme { get; set; } = EditorTheme.Dark;

    /// <summary>Reveals the block (by placing the caret at the given offset) when its chip is clicked.</summary>
    public Action<int>? RequestReveal { get; set; }

    public IEnumerable<(int Start, int End)> CollapsedBlockRanges(TextDocument document)
    {
        if (!HideMarkers) yield break;

        foreach (var (start, end) in _analyzer.DetailsBlocks())
        {
            if (end <= start || _reveal.IsRangeRevealed(start, end)) continue;
            yield return (start, end);
        }
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!HideMarkers) return -1;

        var line = CurrentContext.VisualLine.FirstDocumentLine;
        if (!_analyzer.TryGetDetailsBlock(line.LineNumber, out int start, out int end, out _)) return -1;
        if (line.LineNumber != start || _reveal.IsRangeRevealed(start, end)) return -1;
        if (startOffset > line.Offset) return -1;

        return line.Offset;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var document = CurrentContext.Document;
        var line = CurrentContext.VisualLine.FirstDocumentLine;
        if (!_analyzer.TryGetDetailsBlock(line.LineNumber, out int start, out int end, out string summary)) return null;
        if (line.Offset != offset || line.LineNumber != start) return null;

        var startLine = document.GetLineByNumber(start);
        var endLine = document.GetLineByNumber(end);
        int length = endLine.EndOffset - startLine.Offset;

        return new InlineObjectElement(length, BuildChip(summary, startLine.Offset));
    }

    private FrameworkElement BuildChip(string summary, int revealOffset)
    {
        var triangle = new TextBlock
        {
            Text = "▸",   // ▸
            Foreground = Theme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        };

        var label = new TextBlock
        {
            Text = summary,
            Foreground = Theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(triangle);
        content.Children.Add(label);

        var chip = new Border
        {
            Background = Theme.CodeBackground,
            BorderBrush = Theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 12, 5),
            Margin = new Thickness(0, 2, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = Cursors.Hand,
            ToolTip = "Expand",
            Child = content,
        };

        chip.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            RequestReveal?.Invoke(revealOffset);
        };

        return chip;
    }
}

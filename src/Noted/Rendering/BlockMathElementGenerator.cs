using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Noted.Markdown;

namespace Noted.Rendering;

/// <summary>
/// Collapses a <c>$$ … $$</c> display-math block into a single centred formula, rendered with WpfMath.
/// Multi-line blocks are folded by <see cref="BlockCollapser"/> (via <see cref="ICollapsibleBlockSource"/>)
/// so the element can span them; moving the caret into the block brings the raw LaTeX back.
/// </summary>
public sealed class BlockMathElementGenerator : VisualLineElementGenerator, ICollapsibleBlockSource
{
    private readonly MarkdownAnalyzer _analyzer;
    private readonly RevealTracker _reveal;

    public BlockMathElementGenerator(MarkdownAnalyzer analyzer, RevealTracker reveal)
    {
        _analyzer = analyzer;
        _reveal = reveal;
    }

    public bool HideMarkers { get; set; } = true;

    public EditorTheme Theme { get; set; } = EditorTheme.Dark;

    public double ContentWidth { get; set; }

    public IEnumerable<(int Start, int End)> CollapsedBlockRanges(TextDocument document)
    {
        if (!HideMarkers) yield break;

        foreach (var (start, end) in _analyzer.MathBlocks())
        {
            if (end <= start || _reveal.IsRangeRevealed(start, end)) continue;
            if (MathVisual.CanRender(ExtractLatex(document, start, end))) yield return (start, end);
        }
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!HideMarkers) return -1;

        var line = CurrentContext.VisualLine.FirstDocumentLine;
        if (!_analyzer.TryGetMathBlock(line.LineNumber, out int start, out int end)) return -1;
        if (line.LineNumber != start || _reveal.IsRangeRevealed(start, end)) return -1;
        if (startOffset > line.Offset) return -1;
        if (!MathVisual.CanRender(ExtractLatex(CurrentContext.Document, start, end))) return -1;

        return line.Offset;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var document = CurrentContext.Document;
        var line = CurrentContext.VisualLine.FirstDocumentLine;
        if (!_analyzer.TryGetMathBlock(line.LineNumber, out int start, out int end)) return null;
        if (line.Offset != offset || line.LineNumber != start) return null;

        double scale = CurrentContext.GlobalTextRunProperties.FontRenderingEmSize * 1.4;
        var control = MathVisual.TryBuild(ExtractLatex(document, start, end), scale, Theme.Text);
        if (control is null) return null;

        var startLine = document.GetLineByNumber(start);
        var endLine = document.GetLineByNumber(end);
        int length = endLine.EndOffset - startLine.Offset;

        control.HorizontalAlignment = HorizontalAlignment.Center;
        var container = new Border
        {
            Padding = new Thickness(0, 12, 0, 12),
            Background = Theme.CodeBackground,
            Child = control,
        };
        if (ContentWidth > 0) container.Width = ContentWidth;

        return new InlineObjectElement(length, container);
    }

    private static string ExtractLatex(TextDocument document, int startLine, int endLine)
    {
        var s = document.GetLineByNumber(startLine);
        var e = document.GetLineByNumber(endLine);
        string text = document.GetText(s.Offset, e.EndOffset - s.Offset).Trim();

        if (text.StartsWith("$$", StringComparison.Ordinal)) text = text[2..];
        if (text.EndsWith("$$", StringComparison.Ordinal)) text = text[..^2];
        return text.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}

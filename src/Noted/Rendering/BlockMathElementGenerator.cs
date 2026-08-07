using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    /// <summary>Raised after the LaTeX pill copies, with a short message for a toast.</summary>
    public Action<string>? Copied { get; set; }

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
        control.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid();
        grid.Children.Add(control);
        grid.Children.Add(BuildCopyPill(ExtractRaw(document, start, end)));

        var container = new Border
        {
            Padding = new Thickness(0, 12, 0, 12),
            Background = Theme.CodeBackground,
            Child = grid,
        };
        if (ContentWidth > 0) container.Width = ContentWidth;

        return new InlineObjectElement(length, container);
    }

    /// <summary>A small "LaTeX" tag floated top-right of a rendered formula; clicking it copies the
    /// block's raw LaTeX source, mirroring the language tag on fenced code blocks.</summary>
    private FrameworkElement BuildCopyPill(string latex)
    {
        var label = new TextBlock
        {
            Text = "LaTeX",
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 11.5,
            Foreground = Theme.Muted,
        };

        var pill = new Border
        {
            Background = Theme.SurfaceAlt,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, -6, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.Hand,
            ToolTip = "Copy LaTeX",
            Child = label,
        };

        pill.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;   // keep the caret from jumping into the block (which would un-render it)
            try
            {
                Clipboard.SetText(latex);
                Copied?.Invoke("Copied LaTeX");
            }
            catch (System.Runtime.InteropServices.ExternalException) { }
        };

        return pill;
    }

    /// <summary>The block's raw LaTeX with its <c>$$</c> fences stripped but its line breaks kept,
    /// so a copy round-trips into another editor unchanged.</summary>
    private static string ExtractRaw(TextDocument document, int startLine, int endLine)
    {
        var s = document.GetLineByNumber(startLine);
        var e = document.GetLineByNumber(endLine);
        string text = document.GetText(s.Offset, e.EndOffset - s.Offset).Trim();

        if (text.StartsWith("$$", StringComparison.Ordinal)) text = text[2..];
        if (text.EndsWith("$$", StringComparison.Ordinal)) text = text[..^2];
        return text.Trim('\r', '\n').Trim();
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

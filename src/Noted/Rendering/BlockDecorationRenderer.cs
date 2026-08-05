using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using Noted.Markdown;

namespace Noted.Rendering;

/// <summary>
/// Draws the parts of markdown that aren't characters: code-block panels, the vertical bar
/// beside blockquotes, and the stroke that stands in for a <c>---</c> rule.
/// </summary>
public sealed class BlockDecorationRenderer : IBackgroundRenderer
{
    private const double BarWidth = 3;
    private const double BarGap = 5;

    private readonly MarkdownAnalyzer _analyzer;

    public BlockDecorationRenderer(MarkdownAnalyzer analyzer) => _analyzer = analyzer;

    public EditorTheme Theme { get; set; } = EditorTheme.Dark;

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView.Document is null) return;

        textView.EnsureVisualLines();
        if (textView.VisualLines.Count == 0) return;

        double right = textView.ActualWidth;
        var rulePen = new Pen(Theme.RuleLine, 1.4);
        rulePen.Freeze();

        foreach (var visualLine in textView.VisualLines)
        {
            var info = _analyzer.GetLine(visualLine.FirstDocumentLine.LineNumber);
            if (info.Block == MdStyle.None) continue;

            double top = visualLine.VisualTop - textView.ScrollOffset.Y;
            double height = visualLine.Height;

            if ((info.Block & MdStyle.CodeBlock) != 0)
            {
                drawingContext.DrawRectangle(Theme.CodeBackground, null, new Rect(0, top, right, height + 0.5));
            }

            if ((info.Block & MdStyle.Quote) != 0)
            {
                double contentX = ContentX(textView, visualLine, info.ContentStart);
                double spacing = Math.Min(BarWidth + BarGap, Math.Max(contentX, BarWidth) / info.QuoteDepth);
                for (int depth = 0; depth < info.QuoteDepth; depth++)
                {
                    double x = contentX - (info.QuoteDepth - depth) * spacing;
                    if (x < 0) x = 0;
                    drawingContext.DrawRectangle(Theme.QuoteBar, null, new Rect(x, top, BarWidth, height));
                }
            }

            if ((info.Block & MdStyle.Rule) != 0)
            {
                double y = Math.Round(top + height / 2) + 0.5;
                double left = ContentX(textView, visualLine, info.ContentStart);
                drawingContext.DrawLine(rulePen, new Point(left, y), new Point(Math.Max(left, right - 6), y));
            }
        }
    }

    /// <summary>X coordinate (view space) of a document offset relative to the line start.</summary>
    private static double ContentX(TextView textView, VisualLine visualLine, int relativeOffset)
    {
        int column = visualLine.GetVisualColumn(Math.Max(0, relativeOffset));
        var position = visualLine.GetVisualPosition(column, VisualYPosition.LineTop);
        return position.X - textView.ScrollOffset.X;
    }
}

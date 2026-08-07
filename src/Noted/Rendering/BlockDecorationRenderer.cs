using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using Noted.Markdown;

namespace Noted.Rendering;

/// <summary>
/// Draws the parts of markdown that aren't characters: code-block panels, the vertical bar
/// beside blockquotes, the stroke that stands in for a <c>---</c> rule, and the language tag
/// floated over a collapsed code fence.
/// </summary>
public sealed class BlockDecorationRenderer : IBackgroundRenderer
{
    private const double BarWidth = 3;
    private const double BarGap = 5;

    private readonly MarkdownAnalyzer _analyzer;
    private readonly RevealTracker _reveal;

    /// <summary>Clickable language-tag pills from the last <see cref="Draw"/>, in view coordinates,
    /// each carrying the fenced block's line range so a click can copy it.</summary>
    private readonly List<(Rect Rect, int Start, int End)> _languageTags = new();

    public BlockDecorationRenderer(MarkdownAnalyzer analyzer, RevealTracker reveal)
    {
        _analyzer = analyzer;
        _reveal = reveal;
    }

    public EditorTheme Theme { get; set; } = EditorTheme.Dark;

    public FontFamily MonospaceFont { get; set; } = new("Cascadia Mono, Consolas, Courier New");

    public bool HideMarkers { get; set; } = true;

    /// <summary>
    /// Width of the reading column. Panels and rules stop here rather than running to the
    /// window edge, which the text view's own width would do.
    /// </summary>
    public double ContentWidth { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    /// <summary>If <paramref name="point"/> (in view coordinates) lands on a language-tag pill,
    /// returns the fenced block's inclusive line range.</summary>
    public bool TryHitLanguageTag(Point point, out int startLine, out int endLine)
    {
        foreach (var (rect, start, end) in _languageTags)
        {
            if (rect.Contains(point))
            {
                startLine = start;
                endLine = end;
                return true;
            }
        }

        startLine = endLine = 0;
        return false;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView.Document is null) return;

        textView.EnsureVisualLines();
        _languageTags.Clear();
        if (textView.VisualLines.Count == 0) return;

        double right = ContentWidth > 0 ? Math.Min(ContentWidth, textView.ActualWidth) : textView.ActualWidth;
        var rulePen = new Pen(Theme.RuleLine, 1.4);
        rulePen.Freeze();

        foreach (var visualLine in textView.VisualLines)
        {
            var info = _analyzer.GetLine(visualLine.FirstDocumentLine.LineNumber);
            if (info.Block == MdStyle.None) continue;

            double top = visualLine.VisualTop - textView.ScrollOffset.Y;
            double height = visualLine.Height;

            // A fence or table nested one level inside a blockquote still carries the Quote flag —
            // inset its panel fill past the quote bar instead of drawing under/through it.
            double panelLeft = (info.Block & MdStyle.Quote) != 0 ? ContentX(textView, visualLine, info.ContentStart) : 0;

            if ((info.Block & MdStyle.CodeBlock) != 0)
            {
                drawingContext.DrawRectangle(Theme.CodeBackground, null, new Rect(panelLeft, top, Math.Max(0, right - panelLeft), height + 0.5));

                int lineNumber = visualLine.FirstDocumentLine.LineNumber;
                if (HideMarkers &&
                    _analyzer.TryGetCodeBlock(lineNumber, out int start, out int end, out string language) &&
                    lineNumber == start && language.Length > 0 && !_reveal.IsRangeRevealed(start, end))
                {
                    var pill = DrawLanguageTag(drawingContext, textView, language, top, right);
                    _languageTags.Add((pill, start, end));
                }
            }

            if ((info.Block & MdStyle.Table) != 0)
            {
                drawingContext.DrawRectangle(Theme.Surface, null, new Rect(panelLeft, top, Math.Max(0, right - panelLeft), height + 0.5));
            }

            if ((info.Block & (MdStyle.Quote | MdStyle.Callout)) != 0)
            {
                int lineNumber = visualLine.FirstDocumentLine.LineNumber;
                if (_analyzer.TryGetCalloutBlock(lineNumber, out int cStart, out int cEnd, out var kind))
                {
                    DrawCalloutFrame(drawingContext, Theme.CalloutColor(kind), top, height, right,
                        first: lineNumber == cStart, last: lineNumber == cEnd);
                }
                else if ((info.Block & MdStyle.Quote) != 0)
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
            }

            if ((info.Block & MdStyle.Rule) != 0)
            {
                double y = Math.Round(top + height / 2) + 0.5;
                double left = ContentX(textView, visualLine, info.ContentStart);
                drawingContext.DrawLine(rulePen, new Point(left, y), new Point(Math.Max(left, right - 6), y));
            }
        }
    }

    /// <summary>Derived (fill, border) brushes per callout colour, so a repaint doesn't re-allocate them.</summary>
    private readonly Dictionary<Color, (Brush Fill, Brush Border)> _calloutBrushes = new();

    /// <summary>Draws one line's slice of a callout's surrounding box: a tinted fill, a coloured accent down
    /// the left edge, a hairline down the right, and top/bottom edges on the block's first/last lines.</summary>
    private void DrawCalloutFrame(DrawingContext drawingContext, Brush accent, double top, double height,
        double right, bool first, bool last)
    {
        var color = accent is SolidColorBrush s ? s.Color : Colors.Gray;
        if (!_calloutBrushes.TryGetValue(color, out var brushes))
        {
            Brush Tint(byte a)
            {
                var b = new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B));
                b.Freeze();
                return b;
            }
            brushes = (Tint(0x1E), Tint(0x7A));
            _calloutBrushes[color] = brushes;
        }

        const double accentWidth = 3;
        drawingContext.DrawRectangle(brushes.Fill, null, new Rect(0, top, right, height));
        drawingContext.DrawRectangle(accent, null, new Rect(0, top, accentWidth, height));
        drawingContext.DrawRectangle(brushes.Border, null, new Rect(right - 1, top, 1, height));
        if (first) drawingContext.DrawRectangle(brushes.Border, null, new Rect(0, top, right, 1));
        if (last) drawingContext.DrawRectangle(brushes.Border, null, new Rect(0, top + height - 1, right, 1));
    }

    private Rect DrawLanguageTag(DrawingContext drawingContext, TextView textView, string language, double top, double right)
    {
        double dpi = VisualTreeHelper.GetDpi(textView).PixelsPerDip;
        var text = new FormattedText(
            language,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(MonospaceFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            11.5,
            Theme.Muted,
            dpi);

        const double paddingX = 8, paddingY = 3, margin = 10;
        double pillWidth = text.WidthIncludingTrailingWhitespace + paddingX * 2;
        double pillHeight = text.Height + paddingY * 2;
        double x = right - margin - pillWidth;
        double y = top + 6;

        var pill = new Rect(x, y, pillWidth, pillHeight);
        drawingContext.DrawRoundedRectangle(Theme.SurfaceAlt, null, pill, 5, 5);
        drawingContext.DrawText(text, new Point(x + paddingX, y + paddingY));
        return pill;
    }

    /// <summary>X coordinate (view space) of a document offset relative to the line start.</summary>
    private static double ContentX(TextView textView, VisualLine visualLine, int relativeOffset)
    {
        int column = visualLine.GetVisualColumn(Math.Max(0, relativeOffset));
        var position = visualLine.GetVisualPosition(column, VisualYPosition.LineTop);
        return position.X - textView.ScrollOffset.X;
    }
}

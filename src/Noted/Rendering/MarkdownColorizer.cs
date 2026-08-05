using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Noted.Markdown;

namespace Noted.Rendering;

/// <summary>Paints markdown semantics straight onto the editable text — no preview pane involved.</summary>
public sealed class MarkdownColorizer : DocumentColorizingTransformer
{
    private readonly MarkdownAnalyzer _analyzer;
    private readonly RevealTracker _reveal;

    public MarkdownColorizer(MarkdownAnalyzer analyzer, RevealTracker reveal)
    {
        _analyzer = analyzer;
        _reveal = reveal;
    }

    public EditorTheme Theme { get; set; } = EditorTheme.Dark;

    public FontFamily MonospaceFont { get; set; } = new("Cascadia Mono, Consolas, Courier New");

    protected override void ColorizeLine(DocumentLine line)
    {
        var info = _analyzer.GetLine(line.LineNumber);
        if (info.Tokens.Count == 0 && info.Block == MdStyle.None) return;

        bool revealed = _reveal.IsRevealed(line.LineNumber);
        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;
        if (lineEnd <= lineStart) return;

        // Block-level look first; inline tokens paint over the top of it.
        if (info.HeadingLevel > 0)
        {
            double scale = EditorTheme.HeadingScale[Math.Clamp(info.HeadingLevel, 1, 6) - 1];
            ChangeLinePart(lineStart, lineEnd, el =>
            {
                el.TextRunProperties.SetForegroundBrush(Theme.Heading);
                el.TextRunProperties.SetFontRenderingEmSize(el.TextRunProperties.FontRenderingEmSize * scale);
                Restyle(el, weight: FontWeights.Bold);
            });
        }
        else if ((info.Block & MdStyle.CodeBlock) != 0)
        {
            ChangeLinePart(lineStart, lineEnd, el =>
            {
                el.TextRunProperties.SetTypeface(new Typeface(
                    MonospaceFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal));
                el.TextRunProperties.SetFontRenderingEmSize(el.TextRunProperties.FontRenderingEmSize * 0.94);
                el.TextRunProperties.SetForegroundBrush(Theme.Text);
            });
        }
        else if ((info.Block & MdStyle.Quote) != 0)
        {
            ChangeLinePart(lineStart, lineEnd, el =>
            {
                el.TextRunProperties.SetForegroundBrush(Theme.Quote);
                Restyle(el, style: FontStyles.Italic);
            });
        }

        foreach (var token in info.Tokens)
        {
            int start = lineStart + token.Offset;
            int end = Math.Min(lineStart + token.End, lineEnd);
            if (end <= start) continue;

            var style = token.Style;

            if (token.IsMarker)
            {
                // Rules keep their characters (so the line keeps its height) but fade away
                // completely when the caret is elsewhere; a background renderer draws the stroke.
                if ((style & MdStyle.Rule) != 0)
                {
                    ChangeLinePart(start, end, el =>
                        el.TextRunProperties.SetForegroundBrush(revealed ? Theme.Faint : Brushes.Transparent));
                    continue;
                }

                var markerBrush = (style & MdStyle.Url) != 0 ? Theme.Muted : Theme.Faint;
                ChangeLinePart(start, end, el =>
                {
                    el.TextRunProperties.SetForegroundBrush(markerBrush);
                    el.TextRunProperties.SetTextDecorations(null);
                });
                continue;
            }

            ChangeLinePart(start, end, el => ApplyContentStyle(el, style));
        }
    }

    private void ApplyContentStyle(VisualLineElement el, MdStyle style)
    {
        if ((style & MdStyle.Code) != 0)
        {
            el.TextRunProperties.SetTypeface(new Typeface(
                MonospaceFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal));
            el.TextRunProperties.SetFontRenderingEmSize(el.TextRunProperties.FontRenderingEmSize * 0.92);
            el.TextRunProperties.SetForegroundBrush(Theme.Code);
            el.TextRunProperties.SetBackgroundBrush(Theme.CodeBackground);
            return;
        }

        var weight = (style & MdStyle.Bold) != 0 ? FontWeights.Bold : (FontWeight?)null;
        var fontStyle = (style & MdStyle.Italic) != 0 ? FontStyles.Italic : (FontStyle?)null;
        if (weight is not null || fontStyle is not null) Restyle(el, weight, fontStyle);

        if ((style & MdStyle.Highlight) != 0)
        {
            el.TextRunProperties.SetBackgroundBrush(Theme.HighlightBackground);
            el.TextRunProperties.SetForegroundBrush(Theme.HighlightText);
        }

        if ((style & MdStyle.Link) != 0)
        {
            el.TextRunProperties.SetForegroundBrush(Theme.Link);
            el.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
        }

        if ((style & MdStyle.Strike) != 0)
        {
            el.TextRunProperties.SetForegroundBrush(Theme.Muted);
            el.TextRunProperties.SetTextDecorations(TextDecorations.Strikethrough);
        }
    }

    /// <summary>Rebuilds the element's typeface, keeping whatever weight/style it already carries.</summary>
    private static void Restyle(VisualLineElement el, FontWeight? weight = null, FontStyle? style = null)
    {
        var current = el.TextRunProperties.Typeface;
        el.TextRunProperties.SetTypeface(new Typeface(
            current.FontFamily,
            style ?? current.Style,
            weight ?? current.Weight,
            current.Stretch));
    }
}

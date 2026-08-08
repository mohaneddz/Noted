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
        // A blockquote reveals as a whole block, not one line at a time, so its "> " markers don't
        // pop in and out individually as the caret moves down through it.
        if (!revealed && (info.Block & MdStyle.Quote) != 0 &&
            _analyzer.TryGetQuoteBlock(line.LineNumber, out int quoteStart, out int quoteEnd))
        {
            revealed = _reveal.IsRangeRevealed(quoteStart, quoteEnd);
        }
        var calloutKind = (info.Block & MdStyle.Callout) != 0 ? _analyzer.GetCallout(line.LineNumber) : CalloutKind.None;
        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;
        if (lineEnd <= lineStart) return;

        // Block-level look first; inline tokens paint over the top of it.
        if (info.HeadingLevel > 0)
        {
            int level = Math.Clamp(info.HeadingLevel, 1, 6);
            double scale = EditorTheme.HeadingScale[level - 1];
            var headingBrush = level <= Theme.HeadingColors.Length ? Theme.HeadingColors[level - 1] : Theme.Heading;
            ChangeLinePart(lineStart, lineEnd, el =>
            {
                el.TextRunProperties.SetForegroundBrush(headingBrush);
                el.TextRunProperties.SetFontRenderingEmSize(el.TextRunProperties.FontRenderingEmSize * scale);
                el.TextRunProperties.SetTextDecorations(Theme.HeadingUnderline ? TextDecorations.Underline : null);
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
        else if ((info.Block & MdStyle.TableHeader) != 0)
        {
            ChangeLinePart(lineStart, lineEnd, el =>
            {
                el.TextRunProperties.SetForegroundBrush(Theme.Heading);
                Restyle(el, weight: FontWeights.Bold);
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
                    // Never pass null to SetTextDecorations here: when a heading line is underlined the
                    // whole line already carries a decoration, and AvalonEdit unions the existing
                    // collection with the argument — unioning with null throws and takes the app down.
                    // The heading's underline simply carries through its "#" markers, which is fine.
                });
                continue;
            }

            if ((style & MdStyle.Callout) != 0)
            {
                var calloutBrush = Theme.CalloutColor(calloutKind);
                ChangeLinePart(start, end, el =>
                {
                    el.TextRunProperties.SetForegroundBrush(calloutBrush);
                    Restyle(el, weight: FontWeights.Bold, style: FontStyles.Normal);
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

        if ((style & MdStyle.Footnote) != 0)
        {
            el.TextRunProperties.SetForegroundBrush(Theme.Link);
            el.TextRunProperties.SetFontRenderingEmSize(el.TextRunProperties.FontRenderingEmSize * 0.8);
        }
        else if ((style & MdStyle.Link) != 0)
        {
            el.TextRunProperties.SetForegroundBrush(Theme.Link);
            el.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
        }

        if ((style & MdStyle.Abbreviation) != 0)
        {
            var pen = new Pen(Theme.Muted, 1) { DashStyle = DashStyles.Dot };
            var decoration = new TextDecoration(TextDecorationLocation.Underline, pen, 1,
                TextDecorationUnit.Pixel, TextDecorationUnit.Pixel);
            el.TextRunProperties.SetTextDecorations(new TextDecorationCollection { decoration });
        }

        if ((style & (MdStyle.Sub | MdStyle.Sup)) != 0)
        {
            el.TextRunProperties.SetFontRenderingEmSize(el.TextRunProperties.FontRenderingEmSize * 0.78);
            el.TextRunProperties.SetForegroundBrush(Theme.Muted);
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

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using Noted.Markdown;

namespace Noted.Rendering;

/// <summary>
/// Collapses markdown syntax on lines the user is not editing, and swaps list bullets and
/// task boxes for real glyphs. Everything it hides is still in the document — only the
/// visual line is shortened, so editing, saving and undo are untouched.
/// </summary>
public sealed class MarkdownElementGenerator : VisualLineElementGenerator
{
    private readonly MarkdownAnalyzer _analyzer;
    private readonly RevealTracker _reveal;

    public MarkdownElementGenerator(MarkdownAnalyzer analyzer, RevealTracker reveal)
    {
        _analyzer = analyzer;
        _reveal = reveal;
    }

    public EditorTheme Theme { get; set; } = EditorTheme.Dark;

    public bool HideMarkers { get; set; } = true;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!HideMarkers) return -1;

        var visualLine = CurrentContext.VisualLine;
        var documentLine = visualLine.LastDocumentLine;
        int lineOffset = visualLine.FirstDocumentLine.Offset;

        if (_reveal.IsRevealed(documentLine.LineNumber)) return -1;

        var info = _analyzer.GetLine(documentLine.LineNumber);
        foreach (var token in info.Tokens)
        {
            int absolute = lineOffset + token.Offset;
            if (absolute < startOffset) continue;
            if (Classify(token, info) != Treatment.Keep) return absolute;
        }

        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var visualLine = CurrentContext.VisualLine;
        var documentLine = visualLine.LastDocumentLine;
        int lineOffset = visualLine.FirstDocumentLine.Offset;

        var info = _analyzer.GetLine(documentLine.LineNumber);
        foreach (var token in info.Tokens)
        {
            if (lineOffset + token.Offset != offset) continue;

            return Classify(token, info) switch
            {
                Treatment.Hide => new HiddenTextElement(token.Length),
                Treatment.QuoteIndent => Glyph("  ", token.Length, Theme.Quote),
                Treatment.Bullet => Glyph("•", token.Length, Theme.Accent),
                Treatment.TaskOpen => Glyph("☐ ", token.Length, Theme.Muted),
                Treatment.TaskDone => Glyph("☑ ", token.Length, Theme.Accent),
                _ => null,
            };
        }

        return null;
    }

    private enum Treatment { Keep, Hide, QuoteIndent, Bullet, TaskOpen, TaskDone }

    private static Treatment Classify(MdToken token, MdLine info)
    {
        if (!token.IsMarker) return Treatment.Keep;

        // Horizontal rules and code fences are drawn, not hidden — collapsing them
        // would leave a zero-height line behind.
        if ((token.Style & (MdStyle.Rule | MdStyle.CodeBlock)) != 0) return Treatment.Keep;

        // "> " shrinks to a blank indent rather than vanishing, leaving room for the quote bar.
        if ((token.Style & MdStyle.Quote) != 0)
            return info.AllMarkers ? Treatment.Keep : Treatment.QuoteIndent;

        if ((token.Style & MdStyle.Task) != 0)
            return (token.Style & MdStyle.TaskChecked) != 0 ? Treatment.TaskDone : Treatment.TaskOpen;

        if ((token.Style & MdStyle.Bullet) != 0) return Treatment.Bullet;

        // Ordered-list numbers stay: they carry meaning a glyph can't replace.
        if ((token.Style & MdStyle.ListMarker) != 0) return Treatment.Keep;

        return info.AllMarkers ? Treatment.Keep : Treatment.Hide;
    }

    private VisualLineElement Glyph(string glyph, int documentLength, Brush brush)
    {
        var properties = CurrentContext.GlobalTextRunProperties;
        double dpi = VisualTreeHelper.GetDpi(CurrentContext.TextView).PixelsPerDip;

        var text = new FormattedText(
            glyph,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            properties.Typeface,
            properties.FontRenderingEmSize,
            brush,
            dpi);

        return new GlyphTextElement(text, documentLength);
    }
}

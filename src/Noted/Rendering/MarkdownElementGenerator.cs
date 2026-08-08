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

        int lineNumber = documentLine.LineNumber;
        bool revealed = _reveal.IsRevealed(lineNumber);

        var info = _analyzer.GetLine(lineNumber);
        foreach (var token in info.Tokens)
        {
            int absolute = lineOffset + token.Offset;
            if (absolute < startOffset) continue;
            if (Classify(token, info, lineNumber, revealed) != Treatment.Keep) return absolute;
        }

        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var visualLine = CurrentContext.VisualLine;
        var documentLine = visualLine.LastDocumentLine;
        int lineOffset = visualLine.FirstDocumentLine.Offset;
        int lineNumber = documentLine.LineNumber;
        bool revealed = _reveal.IsRevealed(lineNumber);

        var info = _analyzer.GetLine(lineNumber);
        foreach (var token in info.Tokens)
        {
            if (lineOffset + token.Offset != offset) continue;

            return Classify(token, info, lineNumber, revealed) switch
            {
                Treatment.Hide => new HiddenTextElement(token.Length),
                Treatment.QuoteIndent => Glyph("  ", token.Length, Theme.Quote),
                Treatment.Bullet => Glyph("•", token.Length, Theme.Accent),
                Treatment.TaskOpen => Glyph("☐ ", token.Length, Theme.Muted),
                Treatment.TaskDone => Glyph("☑ ", token.Length, Theme.Accent),
                Treatment.TablePipe => Glyph("│", token.Length, Theme.Faint),
                Treatment.Subscript => Script(offset, token.Length, up: false),
                Treatment.Superscript => Script(offset, token.Length, up: true),
                _ => null,
            };
        }

        return null;
    }

    private enum Treatment { Keep, Hide, QuoteIndent, Bullet, TaskOpen, TaskDone, TablePipe, Subscript, Superscript }

    private Treatment Classify(MdToken token, MdLine info, int lineNumber, bool lineRevealed)
    {
        if (!lineRevealed && !token.IsMarker && (token.Style & (MdStyle.Sub | MdStyle.Sup)) != 0)
            return (token.Style & MdStyle.Sub) != 0 ? Treatment.Subscript : Treatment.Superscript;

        if (!token.IsMarker) return Treatment.Keep;

        // Fence delimiters collapse together with the rest of their block, and pop back
        // as a whole when the caret (or selection) touches any line inside it — the block
        // reveals as a unit rather than one line at a time.
        if ((token.Style & MdStyle.CodeBlock) != 0)
        {
            if (_analyzer.TryGetCodeBlock(lineNumber, out int start, out int end, out _) &&
                _reveal.IsRangeRevealed(start, end))
            {
                return Treatment.Keep;
            }
            return Treatment.Hide;
        }

        if (lineRevealed) return Treatment.Keep;

        // Horizontal rules are drawn, not hidden — collapsing one would leave a zero-height
        // line behind. This also covers a table's delimiter row, which is drawn as a rule.
        if ((token.Style & MdStyle.Rule) != 0) return Treatment.Keep;

        // Table pipes stay as dim column separators; the outer border pipes are dropped so the
        // table doesn't open and close with a bar.
        if ((token.Style & MdStyle.Table) != 0)
            return (token.Style & MdStyle.TableEdge) != 0 ? Treatment.Hide : Treatment.TablePipe;

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

    private VisualLineElement Script(int offset, int documentLength, bool up)
    {
        var properties = CurrentContext.GlobalTextRunProperties;
        var document = CurrentContext.Document;
        string content = document.GetText(offset, documentLength);
        double dpi = VisualTreeHelper.GetDpi(CurrentContext.TextView).PixelsPerDip;
        double lineHeight = properties.FontRenderingEmSize;

        var text = new FormattedText(
            content,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            properties.Typeface,
            lineHeight * 0.72,
            Theme.Muted,
            dpi);

        double shift = up ? -lineHeight * 0.32 : lineHeight * 0.18;
        return new ScriptTextElement(text, documentLength, shift, lineHeight);
    }
}

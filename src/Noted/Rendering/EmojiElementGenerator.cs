using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using Noted.Markdown;

namespace Noted.Rendering;

/// <summary>
/// Swaps <c>:shortcode:</c> tokens for the emoji they name (<c>:rocket:</c> → 🚀) on lines the user
/// isn't editing. Like the other live-markdown pieces, the document text is untouched — only the
/// visual line is shortened — and the raw shortcode reappears when the caret lands on its line.
/// </summary>
public sealed class EmojiElementGenerator : VisualLineElementGenerator
{
    private static readonly Regex Pattern = new(@":([a-zA-Z0-9_+-]+):", RegexOptions.Compiled);

    private readonly MarkdownAnalyzer _analyzer;
    private readonly RevealTracker _reveal;

    public EmojiElementGenerator(MarkdownAnalyzer analyzer, RevealTracker reveal)
    {
        _analyzer = analyzer;
        _reveal = reveal;
    }

    public bool HideMarkers { get; set; } = true;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!HideMarkers) return -1;

        var line = CurrentContext.VisualLine.FirstDocumentLine;
        if (_reveal.IsRevealed(line.LineNumber) || _analyzer.IsInsideCodeBlock(line.LineNumber)) return -1;

        string text = CurrentContext.Document.GetText(line.Offset, line.Length);
        int relStart = startOffset - line.Offset;

        foreach (Match match in Pattern.Matches(text))
        {
            if (match.Index < relStart) continue;
            if (Emoji.TryGet(match.Groups[1].Value, out _)) return line.Offset + match.Index;
        }

        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var line = CurrentContext.VisualLine.FirstDocumentLine;
        string text = CurrentContext.Document.GetText(line.Offset, line.Length);

        foreach (Match match in Pattern.Matches(text))
        {
            if (line.Offset + match.Index != offset) continue;
            if (!Emoji.TryGet(match.Groups[1].Value, out string glyph)) continue;

            return BuildGlyph(glyph, match.Length);
        }

        return null;
    }

    private VisualLineElement BuildGlyph(string glyph, int documentLength)
    {
        var properties = CurrentContext.GlobalTextRunProperties;
        double dpi = VisualTreeHelper.GetDpi(CurrentContext.TextView).PixelsPerDip;

        var typeface = new Typeface(
            new FontFamily("Segoe UI Emoji"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        var text = new FormattedText(
            glyph,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            properties.FontRenderingEmSize,
            properties.ForegroundBrush,
            dpi);

        return new GlyphTextElement(text, documentLength);
    }
}

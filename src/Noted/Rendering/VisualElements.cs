using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using ICSharpCode.AvalonEdit.Rendering;

namespace Noted.Rendering;

/// <summary>
/// A run of document text that occupies no horizontal space. This is how markdown syntax
/// disappears without the document changing: the characters are still there for editing,
/// saving and undo — they just aren't drawn.
/// </summary>
public sealed class HiddenTextElement : VisualLineElement
{
    public HiddenTextElement(int documentLength) : base(1, documentLength)
    {
    }

    public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        => new EmbeddedRun(null, TextRunProperties);
}

/// <summary>Replaces a stretch of document text with a drawn glyph, e.g. <c>-</c> becoming a bullet.</summary>
public sealed class GlyphTextElement : VisualLineElement
{
    private readonly FormattedText _glyph;

    public GlyphTextElement(FormattedText glyph, int documentLength) : base(1, documentLength)
        => _glyph = glyph;

    public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        => new EmbeddedRun(_glyph, TextRunProperties);
}

/// <summary>
/// Shared inline object for the two elements above. A null glyph collapses to zero width.
/// Line breaking is restrained on both sides because these runs sit inside words.
/// </summary>
internal sealed class EmbeddedRun : TextEmbeddedObject
{
    private readonly FormattedText? _glyph;
    private readonly TextRunProperties _properties;

    public EmbeddedRun(FormattedText? glyph, TextRunProperties properties)
    {
        _glyph = glyph;
        _properties = properties;
    }

    public override LineBreakCondition BreakBefore => LineBreakCondition.BreakRestrained;

    public override LineBreakCondition BreakAfter => LineBreakCondition.BreakRestrained;

    public override bool HasFixedSize => true;

    public override CharacterBufferReference CharacterBufferReference => default;

    public override int Length => 1;

    public override TextRunProperties Properties => _properties;

    public override TextEmbeddedObjectMetrics Format(double remainingParagraphWidth)
    {
        if (_glyph is null)
        {
            double height = _properties.FontRenderingEmSize;
            return new TextEmbeddedObjectMetrics(0, height, height * 0.8);
        }

        return new TextEmbeddedObjectMetrics(
            _glyph.WidthIncludingTrailingWhitespace,
            _glyph.Height,
            _glyph.Baseline);
    }

    public override Rect ComputeBoundingBox(bool rightToLeft, bool sideways)
    {
        if (_glyph is null) return Rect.Empty;
        return new Rect(0, -_glyph.Baseline, _glyph.WidthIncludingTrailingWhitespace, _glyph.Height);
    }

    public override void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways)
    {
        // origin sits on the baseline; FormattedText draws from its top-left.
        if (_glyph is not null) drawingContext.DrawText(_glyph, new Point(origin.X, origin.Y - _glyph.Baseline));
    }
}

/// <summary>Draws a run of text shrunk and shifted off the baseline — subscript or superscript.
/// The underlying document text is unchanged; only the drawn position moves.</summary>
public sealed class ScriptTextElement : VisualLineElement
{
    private readonly FormattedText _text;
    private readonly double _shift;
    private readonly double _lineHeight;

    public ScriptTextElement(FormattedText text, int documentLength, double shift, double lineHeight)
        : base(1, documentLength)
    {
        _text = text;
        _shift = shift;
        _lineHeight = lineHeight;
    }

    public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        => new ScriptRun(_text, TextRunProperties, _shift, _lineHeight);
}

internal sealed class ScriptRun : TextEmbeddedObject
{
    private readonly FormattedText _text;
    private readonly TextRunProperties _properties;
    private readonly double _shift;
    private readonly double _lineHeight;

    public ScriptRun(FormattedText text, TextRunProperties properties, double shift, double lineHeight)
    {
        _text = text;
        _properties = properties;
        _shift = shift;
        _lineHeight = lineHeight;
    }

    public override LineBreakCondition BreakBefore => LineBreakCondition.BreakRestrained;

    public override LineBreakCondition BreakAfter => LineBreakCondition.BreakRestrained;

    public override bool HasFixedSize => true;

    public override CharacterBufferReference CharacterBufferReference => default;

    public override int Length => 1;

    public override TextRunProperties Properties => _properties;

    public override TextEmbeddedObjectMetrics Format(double remainingParagraphWidth)
        => new(_text.WidthIncludingTrailingWhitespace, _lineHeight, _lineHeight * 0.8);

    public override Rect ComputeBoundingBox(bool rightToLeft, bool sideways)
        => new(0, -_lineHeight * 0.8, _text.WidthIncludingTrailingWhitespace, _lineHeight);

    public override void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways)
        => drawingContext.DrawText(_text, new Point(origin.X, origin.Y - _text.Baseline + _shift));
}

namespace Noted.Markdown;

/// <summary>
/// Visual roles a slice of a line can play. A span is either syntax (<see cref="Marker"/>)
/// or rendered content; the remaining flags describe how it should look.
/// </summary>
[Flags]
public enum MdStyle
{
    None = 0,
    Marker = 1 << 0,
    Bold = 1 << 1,
    Italic = 1 << 2,
    Strike = 1 << 3,
    Code = 1 << 4,
    Link = 1 << 5,
    Url = 1 << 6,
    Highlight = 1 << 7,
    Heading = 1 << 8,
    Quote = 1 << 9,
    ListMarker = 1 << 10,
    Rule = 1 << 11,
    CodeBlock = 1 << 12,
    Task = 1 << 13,
    TaskChecked = 1 << 14,
    Image = 1 << 15,
    Bullet = 1 << 16,
}

/// <summary>A styled slice of a single line. <see cref="Offset"/> is relative to the line start.</summary>
public readonly record struct MdToken(int Offset, int Length, MdStyle Style)
{
    public int End => Offset + Length;
    public bool IsMarker => (Style & MdStyle.Marker) != 0;
}

/// <summary>Everything the renderers need to know about one line of markdown.</summary>
public sealed class MdLine
{
    public static readonly MdLine Empty = new() { Tokens = [] };

    /// <summary>Block-level role of the whole line (heading / quote / rule / code block / list).</summary>
    public MdStyle Block { get; init; }

    public int HeadingLevel { get; init; }

    public int QuoteDepth { get; init; }

    /// <summary>Column where the line's prose starts, after block prefixes. Used to place the quote bar.</summary>
    public int ContentStart { get; init; }

    public required IReadOnlyList<MdToken> Tokens { get; init; }

    /// <summary>True when the line consists purely of syntax; hiding it would collapse the line to nothing.</summary>
    public bool AllMarkers { get; init; }
}

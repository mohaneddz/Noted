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
    Table = 1 << 17,
    TableHeader = 1 << 18,
    TableDelimiter = 1 << 19,
    TableEdge = 1 << 20,
    Callout = 1 << 21,
    Footnote = 1 << 22,
    Sub = 1 << 23,
    Sup = 1 << 24,
    Abbreviation = 1 << 25,
    DefinitionTerm = 1 << 26,
    DefinitionMarker = 1 << 27,
}

/// <summary>Per-column text alignment, read from a table's <c>:---</c> / <c>:--:</c> / <c>---:</c> delimiter row.</summary>
public enum ColumnAlign { None, Left, Center, Right }

/// <summary>The GitHub-style admonition kinds, as written in a <c>&gt; [!NOTE]</c> blockquote header.</summary>
public enum CalloutKind { None, Note, Tip, Important, Warning, Caution }

public static class Callout
{
    /// <summary>Maps a bang-label (<c>NOTE</c>, <c>tip</c>, …) to its kind, or <see cref="CalloutKind.None"/>
    /// for anything outside the recognised set — those stay literal <c>[!text]</c>.</summary>
    public static CalloutKind Parse(ReadOnlySpan<char> type) => type switch
    {
        _ when type.Equals("NOTE", StringComparison.OrdinalIgnoreCase) => CalloutKind.Note,
        _ when type.Equals("TIP", StringComparison.OrdinalIgnoreCase) => CalloutKind.Tip,
        _ when type.Equals("IMPORTANT", StringComparison.OrdinalIgnoreCase) => CalloutKind.Important,
        _ when type.Equals("WARNING", StringComparison.OrdinalIgnoreCase) => CalloutKind.Warning,
        _ when type.Equals("CAUTION", StringComparison.OrdinalIgnoreCase) => CalloutKind.Caution,
        _ => CalloutKind.None,
    };

    /// <summary>The label shown in place of <c>[!NOTE]</c> — a friendly title-cased word, no brackets.</summary>
    public static string Label(CalloutKind kind) => kind switch
    {
        CalloutKind.Note => "Note",
        CalloutKind.Tip => "Tip",
        CalloutKind.Important => "Important",
        CalloutKind.Warning => "Warning",
        CalloutKind.Caution => "Caution",
        _ => string.Empty,
    };
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

using ICSharpCode.AvalonEdit.Document;

namespace Noted.Markdown;

/// <summary>
/// Caches <see cref="MdLine"/> results for a document and owns the only piece of
/// cross-line state markdown really needs: which lines sit inside a fenced code block.
/// Results are computed lazily, so only lines that actually get painted are scanned.
/// </summary>
public sealed class MarkdownAnalyzer
{
    private enum Fence : byte { None, Delimiter, Inside }

    private enum TableRole : byte { None, Header, Delimiter, Row }

    private enum Setext : byte { None, Heading1, Heading2, Underline }

    /// <summary>One fenced code block, from its opening delimiter line to its closing one.</summary>
    private readonly record struct FenceBlock(int StartLine, int EndLine, string Language);

    private TextDocument? _document;
    private MdLine?[] _cache = [];
    private Fence[] _fences = [];
    private TableRole[] _tables = [];
    private Setext[] _setext = [];
    private CalloutKind[] _callouts = [];
    private int[] _blockStart = [];
    private List<FenceBlock> _blocks = [];
    private bool _stale = true;

    public void Attach(TextDocument? document)
    {
        if (ReferenceEquals(_document, document)) return;

        if (_document is not null) _document.Changed -= OnChanged;
        _document = document;
        if (_document is not null) _document.Changed += OnChanged;

        Invalidate();
    }

    public void Invalidate() => _stale = true;

    private void OnChanged(object? sender, DocumentChangeEventArgs e) => _stale = true;

    public MdLine GetLine(int lineNumber)
    {
        if (_document is null) return MdLine.Empty;

        EnsureFresh();
        if (lineNumber < 1 || lineNumber >= _cache.Length) return MdLine.Empty;

        return _cache[lineNumber] ??= ScanLine(lineNumber);
    }

    public bool IsInsideCodeBlock(int lineNumber)
    {
        if (_document is null) return false;

        EnsureFresh();
        return lineNumber >= 1 && lineNumber < _fences.Length && _fences[lineNumber] != Fence.None;
    }

    /// <summary>
    /// If <paramref name="lineNumber"/> is any line of a fenced code block — delimiter or
    /// content — returns the block's full line range (inclusive) and its language tag.
    /// </summary>
    public bool TryGetCodeBlock(int lineNumber, out int startLine, out int endLine, out string language)
    {
        EnsureFresh();

        if (lineNumber >= 1 && lineNumber < _blockStart.Length && _blockStart[lineNumber] > 0)
        {
            var block = _blocks[_blockStart[lineNumber] - 1];
            startLine = block.StartLine;
            endLine = block.EndLine;
            language = block.Language;
            return true;
        }

        startLine = endLine = 0;
        language = string.Empty;
        return false;
    }

    private MdLine ScanLine(int lineNumber)
    {
        var line = _document!.GetLineByNumber(lineNumber);
        string text = _document.GetText(line.Offset, line.Length);

        if (_fences[lineNumber] == Fence.Delimiter) return MarkdownScanner.ScanFenceDelimiter(text);
        if (_fences[lineNumber] == Fence.Inside) return MarkdownScanner.ScanFencedContent(text);

        switch (_setext[lineNumber])
        {
            case Setext.Heading1: return MarkdownScanner.ScanSetextHeading(text, 1);
            case Setext.Heading2: return MarkdownScanner.ScanSetextHeading(text, 2);
            case Setext.Underline: return MarkdownScanner.ScanSetextUnderline(text);
        }

        return _tables[lineNumber] switch
        {
            TableRole.Header => MarkdownScanner.ScanTableRow(text, header: true),
            TableRole.Delimiter => MarkdownScanner.ScanTableDelimiter(text),
            TableRole.Row => MarkdownScanner.ScanTableRow(text, header: false),
            _ => MarkdownScanner.Scan(text),
        };
    }

    private void EnsureFresh()
    {
        if (!_stale) return;
        _stale = false;

        int lineCount = _document!.LineCount;
        _cache = new MdLine?[lineCount + 1];
        _fences = new Fence[lineCount + 1];
        _tables = new TableRole[lineCount + 1];
        _setext = new Setext[lineCount + 1];
        _callouts = new CalloutKind[lineCount + 1];
        _blockStart = new int[lineCount + 1];
        _blocks = [];

        bool inFence = false;
        char fenceChar = '`';
        int fenceLength = 0;
        int openLine = 0;
        string language = string.Empty;

        for (int n = 1; n <= lineCount; n++)
        {
            var line = _document.GetLineByNumber(n);
            int run = LeadingFenceRun(_document, line, out char c);

            if (!inFence)
            {
                if (run >= 3)
                {
                    inFence = true;
                    fenceChar = c;
                    fenceLength = run;
                    openLine = n;
                    language = ExtractLanguage(_document, line, run);
                    _fences[n] = Fence.Delimiter;
                }
            }
            else if (run >= fenceLength && c == fenceChar)
            {
                inFence = false;
                _fences[n] = Fence.Delimiter;
                CloseBlock(openLine, n, language);
            }
            else
            {
                _fences[n] = Fence.Inside;
            }

            // Tables live outside fences. A delimiter row (|---|:--:|) turns the line above it
            // into a header and every non-blank line below it into a body row, until a blank line.
            if (_fences[n] == Fence.None && _tables[n] == TableRole.None)
            {
                if (n > 1 && _fences[n - 1] == Fence.None && _tables[n - 1] == TableRole.None &&
                    IsTableDelimiter(TextOf(n)) && HasContentAndPipe(TextOf(n - 1)))
                {
                    _tables[n - 1] = TableRole.Header;
                    _tables[n] = TableRole.Delimiter;
                }
                else if (n > 1 && _tables[n - 1] is TableRole.Delimiter or TableRole.Row &&
                         TextOf(n).Trim().Length > 0)
                {
                    _tables[n] = TableRole.Row;
                }
            }

            // Setext headings: a run of '=' (h1) or '-' (h2) directly under a plain paragraph line
            // turns that paragraph into a heading and itself into the heading's underline rule.
            if (_fences[n] == Fence.None && _tables[n] == TableRole.None && _setext[n] == Setext.None &&
                n > 1 && _fences[n - 1] == Fence.None && _tables[n - 1] == TableRole.None &&
                _setext[n - 1] == Setext.None && SetextUnderlineLevel(TextOf(n)) is int level &&
                IsPlainParagraph(TextOf(n - 1)))
            {
                _setext[n - 1] = level == 1 ? Setext.Heading1 : Setext.Heading2;
                _setext[n] = Setext.Underline;
            }
        }

        string TextOf(int number)
        {
            var l = _document.GetLineByNumber(number);
            return _document.GetText(l.Offset, l.Length);
        }

        // Callouts: a "> [!NOTE]" header tints its whole blockquote, so the coloured bar runs the
        // full height of the admonition rather than only its first line.
        for (int n = 1; n <= lineCount; n++)
        {
            if (_fences[n] != Fence.None || _callouts[n] != CalloutKind.None) continue;

            var kind = CalloutHeaderKind(TextOf(n));
            if (kind == CalloutKind.None) continue;
            if (n > 1 && _fences[n - 1] == Fence.None && IsBlockquote(TextOf(n - 1))) continue;  // not the first line

            for (int m = n; m <= lineCount && _fences[m] == Fence.None && IsBlockquote(TextOf(m)); m++)
                _callouts[m] = kind;
        }

        // An unclosed fence still gets a block, running to the end of the document.
        if (inFence) CloseBlock(openLine, lineCount, language);

        void CloseBlock(int start, int end, string lang)
        {
            _blocks.Add(new FenceBlock(start, end, lang));
            int index = _blocks.Count;
            for (int n = start; n <= end; n++) _blockStart[n] = index;
        }
    }

    /// <summary>True if the line is a GFM table delimiter: pipe-separated cells of <c>:?-+:?</c>,
    /// with at least one pipe (so it can never be confused with a <c>---</c> rule or setext underline).</summary>
    private static bool IsTableDelimiter(string text)
    {
        int i = 0;
        int end = text.Length;
        while (end > i && char.IsWhiteSpace(text[end - 1])) end--;
        while (i < end && char.IsWhiteSpace(text[i])) i++;
        if (i >= end) return false;

        bool sawPipe = false, sawDashInCell = false, sawDashOverall = false, cellHasContent = false;
        for (; i < end; i++)
        {
            char c = text[i];
            switch (c)
            {
                case '|':
                    if (cellHasContent && !sawDashInCell) return false;
                    sawPipe = true;
                    sawDashInCell = false;
                    cellHasContent = false;
                    break;
                case '-':
                    sawDashInCell = true;
                    sawDashOverall = true;
                    cellHasContent = true;
                    break;
                case ':':
                    cellHasContent = true;
                    break;
                case ' ':
                case '\t':
                    break;
                default:
                    return false;
            }
        }

        if (cellHasContent && !sawDashInCell) return false;
        return sawPipe && sawDashOverall;
    }

    /// <summary>The callout kind of a blockquote line's admonition tag, if it opens with one.</summary>
    public CalloutKind GetCallout(int lineNumber)
    {
        if (_document is null) return CalloutKind.None;
        EnsureFresh();
        return lineNumber >= 1 && lineNumber < _callouts.Length ? _callouts[lineNumber] : CalloutKind.None;
    }

    /// <summary>Reads a line's callout kind by reusing the scanner's own <c>[!TYPE]</c> recognition.</summary>
    private static CalloutKind CalloutHeaderKind(string text)
    {
        var info = MarkdownScanner.Scan(text);
        if ((info.Block & MdStyle.Callout) == 0) return CalloutKind.None;

        foreach (var token in info.Tokens)
            if (!token.IsMarker && (token.Style & MdStyle.Callout) != 0)
                return Callout.Parse(text.AsSpan(token.Offset, token.Length));

        return CalloutKind.None;
    }

    private static bool IsBlockquote(string text)
    {
        int i = 0;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
        return i < text.Length && text[i] == '>';
    }

    /// <summary>Returns 1 for a <c>===</c> underline, 2 for a <c>---</c> underline, or null if the line
    /// is not a pure run of a single setext underline character.</summary>
    private static int? SetextUnderlineLevel(string text)
    {
        int i = 0, end = text.Length;
        while (end > i && (text[end - 1] == ' ' || text[end - 1] == '\t')) end--;
        while (i < end && (text[i] == ' ' || text[i] == '\t')) i++;
        if (i >= end) return null;

        char c = text[i];
        if (c != '=' && c != '-') return null;
        for (int k = i; k < end; k++)
            if (text[k] != c) return null;

        return c == '=' ? 1 : 2;
    }

    /// <summary>True if the line reads as ordinary paragraph prose — the only thing a setext underline
    /// may attach to (not a heading, list, quote, rule, table or blank line).</summary>
    private static bool IsPlainParagraph(string text)
    {
        if (text.Trim().Length == 0) return false;
        var info = MarkdownScanner.Scan(text);
        return info.Block == MdStyle.None && info.HeadingLevel == 0;
    }

    /// <summary>True if the line has visible text and at least one unescaped pipe — the shape of a table row.</summary>
    private static bool HasContentAndPipe(string text)
    {
        bool content = false, pipe = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; content = true; continue; }
            if (text[i] == '|') pipe = true;
            else if (!char.IsWhiteSpace(text[i])) content = true;
        }
        return content && pipe;
    }

    /// <summary>Text after the fence run on its opening line, e.g. <c>```csharp</c> → <c>csharp</c>.</summary>
    private static string ExtractLanguage(TextDocument document, DocumentLine line, int fenceRun)
    {
        int offset = line.Offset;
        int indent = 0;
        while (offset < line.EndOffset && indent < 4 && document.GetCharAt(offset) == ' ') { offset++; indent++; }

        int start = offset + fenceRun;
        string rest = start < line.EndOffset ? document.GetText(start, line.EndOffset - start) : string.Empty;
        return rest.Trim();
    }

    /// <summary>Length of a leading ``` / ~~~ run, ignoring up to three spaces of indentation.</summary>
    private static int LeadingFenceRun(TextDocument document, DocumentLine line, out char fenceChar)
    {
        fenceChar = '`';
        int offset = line.Offset;
        int endOffset = line.EndOffset;

        int indent = 0;
        while (offset < endOffset && indent < 4 && document.GetCharAt(offset) == ' ') { offset++; indent++; }
        if (indent >= 4 || offset >= endOffset) return 0;

        char c = document.GetCharAt(offset);
        if (c is not ('`' or '~')) return 0;

        fenceChar = c;
        int run = 0;
        while (offset + run < endOffset && document.GetCharAt(offset + run) == c) run++;
        return run;
    }
}

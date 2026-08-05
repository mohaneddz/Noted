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

    private TextDocument? _document;
    private MdLine?[] _cache = [];
    private Fence[] _fences = [];
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

    public bool IsInsideCodeBlock(int lineNumber) =>
        lineNumber >= 1 && lineNumber < _fences.Length && _fences[lineNumber] != Fence.None;

    private MdLine ScanLine(int lineNumber)
    {
        var line = _document!.GetLineByNumber(lineNumber);
        string text = _document.GetText(line.Offset, line.Length);

        return _fences[lineNumber] switch
        {
            Fence.Delimiter => MarkdownScanner.ScanFenceDelimiter(text),
            Fence.Inside => MarkdownScanner.ScanFencedContent(text),
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

        bool inFence = false;
        char fenceChar = '`';
        int fenceLength = 0;

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
                    _fences[n] = Fence.Delimiter;
                }
            }
            else if (run >= fenceLength && c == fenceChar)
            {
                inFence = false;
                _fences[n] = Fence.Delimiter;
            }
            else
            {
                _fences[n] = Fence.Inside;
            }
        }
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

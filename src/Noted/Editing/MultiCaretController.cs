using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Noted.Editing;

public readonly record struct CaretRange(int Offset, int Length) : ISegment
{
    public int End => Offset + Length;
    public int EndOffset => End;
}

/// <summary>Secondary carets/selections, with their state recorded in the document's undo group.</summary>
public sealed class MultiCaretController : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private readonly List<CaretRange> _secondary = [];
    private TextDocument? _document;
    private bool _applying;
    private string? _search;
    private bool _wholeWord;

    public MultiCaretController(TextEditor editor)
    {
        _editor = editor;
        _document = editor.Document;
        if (_document is not null) _document.Changed += DocumentChanged;
        editor.DocumentChanged += (_, _) =>
        {
            if (_document is not null) _document.Changed -= DocumentChanged;
            _document = editor.Document;
            if (_document is not null) _document.Changed += DocumentChanged;
            Clear();
        };
        editor.TextArea.SelectionChanged += (_, _) =>
        {
            if (!_applying && _document is not null && _document.UndoStack.AcceptChanges && !_document.IsInUpdate) Clear();
        };
    }

    private void DocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        if (!_applying && _document is not null && _document.UndoStack.AcceptChanges) Clear();
    }

    public Brush CaretBrush { get; set; } = Brushes.White;
    public bool HasSecondaryCarets => _secondary.Count > 0;
    public IReadOnlyList<CaretRange> SecondaryRanges => _secondary;
    public event Action? RangesChanged;
    public KnownLayer Layer => KnownLayer.Caret;
    private CaretRange Primary => new(_editor.SelectionLength > 0 ? _editor.SelectionStart : _editor.CaretOffset, _editor.SelectionLength);

    public void ToggleCaretAt(int offset)
    {
        if (_document is null || offset < 0 || offset > _document.TextLength || offset == _editor.CaretOffset) return;
        _search = null;
        int existing = _secondary.FindIndex(r => r.Offset == offset);
        if (existing >= 0) _secondary.RemoveAt(existing);
        else
        {
            var previous = new CaretRange(_editor.CaretOffset, 0);
            _secondary.Add(previous);
            SetPrimary(new(offset, 0));
        }
        Redraw();
    }

    /// <summary>First invocation selects the word at the caret; subsequent invocations add the
    /// next occurrence, wrapping once and never selecting an occurrence twice.</summary>
    public void SelectNextOccurrence()
    {
        if (_document is null) return;
        string text = _document.Text;
        var primary = Primary;
        if (primary.Length == 0)
        {
            Clear();
            int start = primary.Offset;
            if (start == text.Length || (start < text.Length && !WordChar(text[start]))) start--;
            if (start < 0 || !WordChar(text[start])) return;
            int end = start + 1;
            while (start > 0 && WordChar(text[start - 1])) start--;
            while (end < text.Length && WordChar(text[end])) end++;
            _search = text[start..end]; _wholeWord = true;
            SetPrimary(new(start, end - start));
            return;
        }
        if (_search is null || text.Substring(primary.Offset, primary.Length) != _search)
        {
            _secondary.Clear();
            _search = text.Substring(primary.Offset, primary.Length);
            _wholeWord = _search.All(WordChar) && WordBoundary(text, primary.Offset, primary.End);
        }
        var selected = _secondary.Append(primary).ToArray();
        int next = Find(primary.End, text.Length);
        if (next < 0) next = Find(0, primary.Offset);
        if (next < 0) return;
        _secondary.Add(primary);
        SetPrimary(new(next, _search.Length));
        _editor.ScrollToLine(_document.GetLineByOffset(next).LineNumber);
        Redraw();

        int Find(int from, int end)
        {
            while (from <= end - _search.Length)
            {
                int found = text.IndexOf(_search, from, end - from, StringComparison.Ordinal);
                if (found < 0) return -1;
                int stop = found + _search.Length;
                if ((!_wholeWord || WordBoundary(text, found, stop)) &&
                    !selected.Any(r => found < r.End && stop > r.Offset)) return found;
                from = found + 1;
            }
            return -1;
        }
    }

    private static bool WordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    private static bool WordBoundary(string text, int start, int end) =>
        (start == 0 || !WordChar(text[start - 1])) && (end == text.Length || !WordChar(text[end]));

    public void Clear()
    {
        _search = null;
        if (_secondary.Count == 0) return;
        _secondary.Clear(); Redraw();
    }

    public bool HandleTextInput(string text) => Apply(text, 0);
    public bool HandleBackspace() => Apply("", -1);
    public bool HandleDelete() => Apply("", 1);
    public bool HandleEnter() => Apply(Environment.NewLine, 0);

    private bool Apply(string replacement, int deleteDirection)
    {
        if (_document is null || !HasSecondaryCarets || _editor.IsReadOnly) return false;
        var before = Snapshot();
        var ranges = _secondary.Append(Primary).ToArray();
        var results = new CaretRange[ranges.Length];
        var processed = new List<int>();
        _applying = true;
        var undo = _document.UndoStack;
        undo.StartUndoGroup();
        undo.PushOptional(new SelectionUndo(this, _document, before, onUndo: true));
        try
        {
            using (_document.RunUpdate())
            {
                foreach (int i in Enumerable.Range(0, ranges.Length).OrderByDescending(i => ranges[i].Offset))
                {
                    int start = ranges[i].Offset, length = ranges[i].Length;
                    if (length == 0 && deleteDirection < 0 && start > 0)
                    {
                        length = start >= 2 && ((_document.GetCharAt(start - 2) == '\r' && _document.GetCharAt(start - 1) == '\n') ||
                            (char.IsHighSurrogate(_document.GetCharAt(start - 2)) && char.IsLowSurrogate(_document.GetCharAt(start - 1)))) ? 2 : 1;
                        start -= length;
                    }
                    else if (length == 0 && deleteDirection > 0 && start < _document.TextLength)
                        length = start + 1 < _document.TextLength && ((_document.GetCharAt(start) == '\r' && _document.GetCharAt(start + 1) == '\n') ||
                            (char.IsHighSurrogate(_document.GetCharAt(start)) && char.IsLowSurrogate(_document.GetCharAt(start + 1)))) ? 2 : 1;
                    if (length > 0 || replacement.Length > 0) _document.Replace(start, length, replacement);
                    int delta = replacement.Length - length;
                    foreach (int done in processed) results[done] = results[done] with { Offset = results[done].Offset + delta };
                    results[i] = new(start + replacement.Length, 0);
                    processed.Add(i);
                }
            }
            Restore(new(results[..^1], results[^1], null, false));
            undo.PushOptional(new SelectionUndo(this, _document, Snapshot(), onUndo: false));
        }
        finally { undo.EndUndoGroup(); _applying = false; }
        return true;
    }

    private sealed record State(CaretRange[] Secondary, CaretRange Primary, string? Search, bool WholeWord);
    private State Snapshot() => new(_secondary.ToArray(), Primary, _search, _wholeWord);
    private void Restore(State state)
    {
        bool previous = _applying; _applying = true;
        try
        {
            _secondary.Clear(); _secondary.AddRange(state.Secondary.Distinct().Where(r => r != state.Primary));
            _search = state.Search; _wholeWord = state.WholeWord;
            SetPrimary(state.Primary); Redraw();
        }
        finally { _applying = previous; }
    }
    private void SetPrimary(CaretRange range)
    {
        bool previous = _applying; _applying = true;
        try { _editor.Select(range.Offset, range.Length); }
        finally { _applying = previous; }
    }
    private sealed class SelectionUndo(MultiCaretController owner, TextDocument document, State state, bool onUndo) : IUndoableOperation
    {
        public void Undo() { if (onUndo && owner._document == document) owner.Restore(state); }
        public void Redo() { if (!onUndo && owner._document == document) owner.Restore(state); }
    }
    private void Redraw()
    {
        _editor.TextArea.TextView.InvalidateLayer(Layer);
        RangesChanged?.Invoke();
    }

    public static Rect CaretBounds(TextView view, int offset)
    {
        var line = view.Document.GetLineByOffset(offset);
        var visual = view.GetVisualLine(line.LineNumber);
        if (visual is null) return Rect.Empty;
        int column = visual.GetVisualColumn(offset - visual.FirstDocumentLine.Offset);
        var top = visual.GetVisualPosition(column, VisualYPosition.TextTop) - view.ScrollOffset;
        var bottom = visual.GetVisualPosition(column, VisualYPosition.TextBottom) - view.ScrollOffset;
        return new Rect(top.X, top.Y, 1.4, Math.Max(1, bottom.Y - top.Y));
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!HasSecondaryCarets || textView.Document is null || !textView.VisualLinesValid) return;
        foreach (var range in _secondary)
        {
            if (range.Offset < 0 || range.End > textView.Document.TextLength) continue;
            if (range.Length > 0)
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, range))
                    drawingContext.DrawRectangle(_editor.TextArea.SelectionBrush, null, rect);
            var bounds = CaretBounds(textView, range.End);
            if (!bounds.IsEmpty) drawingContext.DrawRectangle(CaretBrush, null, bounds);
        }
    }
}

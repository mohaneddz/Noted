using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;

namespace Noted.Rendering;

/// <summary>
/// Decides which lines currently show their raw markdown. This is the whole trick behind the
/// "Obsidian feel": syntax is hidden everywhere except where the user is actually working —
/// the caret's line, plus anything covered by a selection.
/// </summary>
public sealed class RevealTracker
{
    private TextEditor? _editor;
    private int _caretLine = 1;
    private int _selectionStartLine;
    private int _selectionEndLine = -1;

    public event Action<int, int>? RevealChanged;

    public bool Enabled { get; set; } = true;

    public void Attach(TextEditor editor)
    {
        if (_editor is not null)
        {
            _editor.TextArea.Caret.PositionChanged -= OnCaretChanged;
            _editor.TextArea.SelectionChanged -= OnSelectionChanged;
        }

        _editor = editor;
        _editor.TextArea.Caret.PositionChanged += OnCaretChanged;
        _editor.TextArea.SelectionChanged += OnSelectionChanged;
        Refresh(force: true);
    }

    public bool IsRevealed(int lineNumber)
    {
        if (!Enabled) return true;
        if (lineNumber == _caretLine) return true;
        return lineNumber >= _selectionStartLine && lineNumber <= _selectionEndLine;
    }

    private void OnCaretChanged(object? sender, EventArgs e) => Refresh(force: false);

    private void OnSelectionChanged(object? sender, EventArgs e) => Refresh(force: false);

    private void Refresh(bool force)
    {
        if (_editor is null) return;

        int caretLine = _editor.TextArea.Caret.Line;
        int selStart = 0, selEnd = -1;

        var selection = _editor.TextArea.Selection;
        if (selection is not null && !selection.IsEmpty)
        {
            selStart = selection.StartPosition.Line;
            selEnd = selection.EndPosition.Line;
            if (selStart > selEnd) (selStart, selEnd) = (selEnd, selStart);
        }

        if (!force && caretLine == _caretLine && selStart == _selectionStartLine && selEnd == _selectionEndLine)
            return;

        int dirtyFrom = Min(caretLine, _caretLine);
        int dirtyTo = Max(caretLine, _caretLine);

        if (selEnd >= selStart && selEnd >= 0)
        {
            dirtyFrom = Min(dirtyFrom, selStart);
            dirtyTo = Max(dirtyTo, selEnd);
        }
        if (_selectionEndLine >= _selectionStartLine && _selectionEndLine >= 0)
        {
            dirtyFrom = Min(dirtyFrom, _selectionStartLine);
            dirtyTo = Max(dirtyTo, _selectionEndLine);
        }

        _caretLine = caretLine;
        _selectionStartLine = selStart;
        _selectionEndLine = selEnd;

        RevealChanged?.Invoke(dirtyFrom, dirtyTo);
    }

    private static int Min(int a, int b) => a < b ? a : b;

    private static int Max(int a, int b) => a > b ? a : b;
}

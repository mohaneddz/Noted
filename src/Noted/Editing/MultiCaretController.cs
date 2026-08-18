using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Noted.Editing;

/// <summary>
/// AvalonEdit only tracks one caret. This layers a second set of point carets on top: Alt+click adds
/// one, typing/Backspace/Delete/Enter replay at every caret, and anything that isn't a plain edit
/// (a selection, an unrelated document change, arrow-key navigation) drops back to a single caret.
/// </summary>
public sealed class MultiCaretController : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private readonly List<int> _offsets = [];
    private bool _applying;

    public MultiCaretController(TextEditor editor)
    {
        _editor = editor;
        _editor.Document.Changed += (_, _) =>
        {
            if (!_applying) Clear();
        };
    }

    public Brush CaretBrush { get; set; } = Brushes.White;

    public bool HasSecondaryCarets => _offsets.Count > 0;

    public KnownLayer Layer => KnownLayer.Caret;

    /// <summary>Adds a caret at <paramref name="offset"/>, or removes it if one is already there.</summary>
    public void ToggleCaretAt(int offset)
    {
        if (offset == _editor.CaretOffset) return;

        if (_offsets.Remove(offset))
        {
            Redraw();
            return;
        }

        _offsets.Add(_editor.CaretOffset);
        _offsets.Sort();
        _editor.CaretOffset = offset;
        Redraw();
    }

    public void Clear()
    {
        if (_offsets.Count == 0) return;
        _offsets.Clear();
        Redraw();
    }

    public bool HandleTextInput(string text)
    {
        if (!CanApply()) return false;
        ApplyAtAllCarets((document, offset) =>
        {
            document.Insert(offset, text);
            return offset + text.Length;
        });
        return true;
    }

    public bool HandleBackspace()
    {
        if (!CanApply()) return false;
        ApplyAtAllCarets((document, offset) =>
        {
            if (offset == 0) return 0;
            document.Remove(offset - 1, 1);
            return offset - 1;
        });
        return true;
    }

    public bool HandleDelete()
    {
        if (!CanApply()) return false;
        ApplyAtAllCarets((document, offset) =>
        {
            if (offset >= document.TextLength) return offset;
            document.Remove(offset, 1);
            return offset;
        });
        return true;
    }

    public bool HandleEnter()
    {
        if (!CanApply()) return false;
        string newLine = Environment.NewLine;
        ApplyAtAllCarets((document, offset) =>
        {
            document.Insert(offset, newLine);
            return offset + newLine.Length;
        });
        return true;
    }

    /// <summary>A real selection makes per-caret replacement ambiguous for v1, so bail to a single caret.</summary>
    private bool CanApply()
    {
        if (_offsets.Count == 0) return false;
        if (_editor.TextArea.Selection.IsEmpty) return true;

        Clear();
        return false;
    }

    /// <summary>
    /// Applies the same point edit at the primary caret and every secondary caret. Carets are edited
    /// from the highest document offset down, so an edit never shifts an offset still waiting its turn.
    /// Each edit's length delta is then applied back onto the carets already processed, since those
    /// sit after the edit point and would otherwise drift by one character per keystroke.
    /// </summary>
    private void ApplyAtAllCarets(Func<TextDocument, int, int> edit)
    {
        var document = _editor.Document;
        var all = new List<int>(_offsets) { _editor.CaretOffset };
        int primaryIndex = all.Count - 1;
        var newOffsets = new int[all.Count];

        var order = Enumerable.Range(0, all.Count).OrderByDescending(i => all[i]);
        var processed = new List<int>();

        _applying = true;
        try
        {
            using (document.RunUpdate())
            {
                foreach (int i in order)
                {
                    int lengthBefore = document.TextLength;
                    newOffsets[i] = edit(document, all[i]);
                    int delta = document.TextLength - lengthBefore;

                    if (delta != 0)
                    {
                        foreach (int done in processed) newOffsets[done] += delta;
                    }

                    processed.Add(i);
                }
            }
        }
        finally
        {
            _applying = false;
        }

        _offsets.Clear();
        for (int i = 0; i < primaryIndex; i++) _offsets.Add(newOffsets[i]);
        _offsets.Sort();
        _editor.CaretOffset = newOffsets[primaryIndex];
        Redraw();
    }

    private void Redraw() => _editor.TextArea.TextView.InvalidateLayer(Layer);

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_offsets.Count == 0 || textView.Document is null) return;

        textView.EnsureVisualLines();
        var pen = new Pen(CaretBrush, 1.4);
        pen.Freeze();

        foreach (int offset in _offsets)
        {
            if (offset < 0 || offset > textView.Document.TextLength) continue;

            var line = textView.Document.GetLineByOffset(offset);
            var visualLine = textView.GetVisualLine(line.LineNumber);
            if (visualLine is null) continue;

            int column = visualLine.GetVisualColumn(offset - line.Offset);
            var position = visualLine.GetVisualPosition(column, VisualYPosition.LineTop);
            double x = position.X - textView.ScrollOffset.X;
            double top = position.Y - textView.ScrollOffset.Y;

            drawingContext.DrawLine(pen, new Point(x, top), new Point(x, top + visualLine.Height));
        }
    }
}

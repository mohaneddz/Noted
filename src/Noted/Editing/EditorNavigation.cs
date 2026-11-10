using System.Windows.Input;
using ICSharpCode.AvalonEdit;

namespace Noted.Editing;

public static class EditorNavigation
{
    public static bool Handle(TextEditor editor, Key key, ModifierKeys modifiers)
    {
        if (key is not (Key.Left or Key.Right)) return false;

        int offset;
        if (modifiers == ModifierKeys.Alt)
        {
            var line = editor.Document.GetLineByOffset(editor.CaretOffset);
            offset = key == Key.Left ? line.Offset : line.EndOffset;
        }
        else if (modifiers == ModifierKeys.None && editor.SelectionLength > 0)
        {
            offset = editor.SelectionStart + (key == Key.Right ? editor.SelectionLength : 0);
        }
        else return false;

        editor.Select(offset, 0);
        editor.CaretOffset = offset;
        editor.TextArea.Caret.BringCaretToView();
        return true;
    }
}

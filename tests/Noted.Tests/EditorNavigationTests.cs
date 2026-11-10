using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using Noted.Editing;

namespace Noted.Tests;

public class EditorNavigationTests
{
    [Theory]
    [InlineData(Key.Left, 0)]
    [InlineData(Key.Right, 11)]
    public void ArrowCollapsesSelectionAtRequestedEdge(Key key, int expected) => MultiCaretTests.Sta(() =>
    {
        var editor = new TextEditor { Text = "hello\nworld" };
        editor.SelectAll();
        Assert.True(EditorNavigation.Handle(editor, key, ModifierKeys.None));
        Assert.Equal(expected, editor.CaretOffset);
        Assert.Equal(0, editor.SelectionLength);
    });

    [Theory]
    [InlineData(Key.Left, 6)]
    [InlineData(Key.Right, 11)]
    public void AltArrowMovesToLineBoundary(Key key, int expected) => MultiCaretTests.Sta(() =>
    {
        var editor = new TextEditor { Text = "hello\nworld", CaretOffset = 8 };
        Assert.True(EditorNavigation.Handle(editor, key, ModifierKeys.Alt));
        Assert.Equal(expected, editor.CaretOffset);
    });

    [Fact]
    public void OtherArrowCombinationsAreLeftToEditor() => MultiCaretTests.Sta(() =>
    {
        var editor = new TextEditor { Text = "hello" };
        Assert.False(EditorNavigation.Handle(editor, Key.Left, ModifierKeys.None));
        Assert.False(EditorNavigation.Handle(editor, Key.Right, ModifierKeys.Shift));
    });
}

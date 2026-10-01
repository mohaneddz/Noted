using System.Windows;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Noted.Editing;

namespace Noted.Tests;

public class MultiCaretTests
{
    internal static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "UI test timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void SelectsSuccessiveWholeWordsWrapsAndStopsWithoutDuplicates() => Sta(() =>
    {
        var editor = new TextEditor { Text = "cat catapult cat cat", CaretOffset = 13 };
        var controller = new MultiCaretController(editor);
        controller.SelectNextOccurrence();
        Assert.Equal("cat", editor.SelectedText);
        controller.SelectNextOccurrence(); controller.SelectNextOccurrence(); controller.SelectNextOccurrence();
        Assert.Equal(2, controller.SecondaryRanges.Count);
        Assert.Equal(0, editor.SelectionStart);
        Assert.True(controller.HandleTextInput("dog"));
        Assert.Equal("dog catapult dog dog", editor.Text);
        editor.Undo();
        Assert.Equal("cat catapult cat cat", editor.Text);
        Assert.Equal(0, editor.SelectionStart);
        Assert.Equal(3, editor.SelectionLength);
        Assert.Equal(new[] { new CaretRange(13, 3), new CaretRange(17, 3) }, controller.SecondaryRanges);
        editor.Redo();
        Assert.Equal("dog catapult dog dog", editor.Text);
        Assert.Equal(3, editor.CaretOffset);
        Assert.All(controller.SecondaryRanges, r => Assert.Equal(0, r.Length));
    });

    [Fact]
    public void AltClickCaretsAreRestoredAfterMultipleEditsUndoAndRedo() => Sta(() =>
    {
        var editor = new TextEditor { Text = "ab cd ef", CaretOffset = 1 };
        var controller = new MultiCaretController(editor);
        controller.ToggleCaretAt(4); controller.ToggleCaretAt(7);
        controller.HandleTextInput("XX");
        Assert.Equal("aXXb cXXd eXXf", editor.Text);
        controller.HandleBackspace();
        Assert.Equal("aXb cXd eXf", editor.Text);
        editor.Undo(); editor.Undo();
        Assert.Equal("ab cd ef", editor.Text);
        Assert.Equal(7, editor.CaretOffset);
        Assert.Equal(new[] { new CaretRange(1, 0), new CaretRange(4, 0) }, controller.SecondaryRanges);
        editor.Redo();
        Assert.Equal(13, editor.CaretOffset);
        Assert.Equal(new[] { new CaretRange(3, 0), new CaretRange(8, 0) }, controller.SecondaryRanges);
    });

    [Fact]
    public void ControllerFollowsDocumentSwitchesAndIgnoresOldDocumentEdits() => Sta(() =>
    {
        var editor = new TextEditor { Text = "first note" };
        var old = editor.Document;
        var controller = new MultiCaretController(editor);
        editor.Document = new TextDocument("abc def");
        editor.CaretOffset = 1; controller.ToggleCaretAt(5);
        old.Insert(0, "x");
        Assert.True(controller.HasSecondaryCarets);
        controller.HandleTextInput("z"); editor.Undo();
        Assert.Equal(5, editor.CaretOffset);
        Assert.Equal(new CaretRange(1, 0), Assert.Single(controller.SecondaryRanges));
        editor.Document.Insert(0, "external");
        Assert.False(controller.HasSecondaryCarets);
        editor.Document = null;
        controller.SelectNextOccurrence();
        Assert.False(controller.HandleTextInput("ignored"));
    });

    [Fact]
    public void SecondaryCaretUsesWrappedTextRowHeight() => Sta(() =>
    {
        var editor = new TextEditor { Text = string.Join(" ", Enumerable.Repeat("wrapped words", 20)), FontSize = 20 };
        var view = editor.TextArea.TextView; ((System.Windows.Controls.Primitives.IScrollInfo)view).CanHorizontallyScroll = false;
        view.Measure(new Size(180, 900)); view.Arrange(new Rect(0, 0, 180, 900)); view.UpdateLayout(); view.EnsureVisualLines();
        var visual = Assert.Single(view.VisualLines);
        Assert.True(visual.TextLines.Count > 1);
        var bounds = MultiCaretController.CaretBounds(view, 40);
        Assert.InRange(bounds.Height, 10, 40);
        Assert.True(bounds.Height < visual.Height / 2);
    });
}

using System.Windows;
using System.Windows.Media.TextFormatting;
using ICSharpCode.AvalonEdit.Rendering;

namespace Noted.Rendering;

/// <summary>Preview cards force a visual line break on both sides without adding text to the note.</summary>
public sealed class LinkObjectElement(int length, UIElement element, bool block, bool startsLine = false)
    : VisualLineElement(block ? (startsLine ? 2 : 3) : 1, length)
{
    public bool IsBlock => block;
    public UIElement Element => element;
    public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
    {
        int column = startVisualColumn - VisualColumn;
        // Actual text-formatting line-break runs are necessary: BreakAlways on an embedded run
        // is ignored when word wrap is disabled. These runs consume no extra document characters.
        int objectColumn = block && !startsLine ? 1 : 0;
        return column == objectColumn ? new InlineObjectRun(1, TextRunProperties, Element) : new TextEndOfLine(1);
    }
}

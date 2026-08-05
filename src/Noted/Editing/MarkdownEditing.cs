using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;

namespace Noted.Editing;

/// <summary>Typing conveniences that make markdown feel native rather than hand-typed.</summary>
public static partial class MarkdownEditing
{
    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<quote>(?:>[ ]?)*)(?:(?<bullet>[-*+])[ \t]+|(?<number>\d{1,9})(?<delim>[.)])[ \t]+)?(?<task>\[[ xX-]\][ \t]+)?")]
    private static partial Regex ListPrefixRegex { get; }

    /// <summary>
    /// Continues lists and blockquotes on Enter, and clears the prefix when the user
    /// presses Enter on an item they never filled in.
    /// </summary>
    public static bool TryContinueList(TextEditor editor)
    {
        if (!editor.TextArea.Selection.IsEmpty) return false;

        var document = editor.Document;
        var line = document.GetLineByNumber(editor.TextArea.Caret.Line);
        string text = document.GetText(line);

        var match = ListPrefixRegex.Match(text);
        int prefixLength = match.Length;
        if (prefixLength == 0) return false;

        bool hasQuote = match.Groups["quote"].Length > 0;
        bool hasList = match.Groups["bullet"].Success || match.Groups["number"].Success;
        if (!hasQuote && !hasList) return false;

        int caretColumn = editor.CaretOffset - line.Offset;
        if (caretColumn < prefixLength) return false;

        // Enter on an empty item ends the list instead of adding another one.
        if (text.Length == prefixLength)
        {
            document.Replace(line.Offset, prefixLength, string.Empty);
            return true;
        }

        string continuation = BuildContinuation(match);
        document.Insert(editor.CaretOffset, Environment.NewLine + continuation);
        return true;
    }

    private static string BuildContinuation(Match match)
    {
        string prefix = match.Groups["indent"].Value + match.Groups["quote"].Value;

        if (match.Groups["bullet"].Success)
        {
            prefix += match.Groups["bullet"].Value + " ";
        }
        else if (match.Groups["number"].Success)
        {
            long next = long.Parse(match.Groups["number"].Value) + 1;
            prefix += next + match.Groups["delim"].Value + " ";
        }

        if (match.Groups["task"].Success) prefix += "[ ] ";

        return prefix;
    }

    /// <summary>Wraps or unwraps the selection (or the word at the caret) with an inline marker.</summary>
    public static void ToggleInline(TextEditor editor, string marker)
    {
        var document = editor.Document;
        var (start, length) = GetTargetSegment(editor);

        string inner = document.GetText(start, length);
        int markerLength = marker.Length;

        bool wrappedInside = inner.Length >= markerLength * 2 &&
                             inner.StartsWith(marker, StringComparison.Ordinal) &&
                             inner.EndsWith(marker, StringComparison.Ordinal);

        bool wrappedOutside = !wrappedInside &&
                              start >= markerLength &&
                              start + length + markerLength <= document.TextLength &&
                              document.GetText(start - markerLength, markerLength) == marker &&
                              document.GetText(start + length, markerLength) == marker;

        using (document.RunUpdate())
        {
            if (wrappedInside)
            {
                string stripped = inner[markerLength..^markerLength];
                document.Replace(start, length, stripped);
                Select(editor, start, stripped.Length);
            }
            else if (wrappedOutside)
            {
                document.Remove(start + length, markerLength);
                document.Remove(start - markerLength, markerLength);
                Select(editor, start - markerLength, length);
            }
            else
            {
                document.Insert(start + length, marker);
                document.Insert(start, marker);
                Select(editor, start + markerLength, length);
            }
        }
    }

    /// <summary>Applies, cycles, or clears a line-leading marker such as <c>## </c> or <c>&gt; </c>.</summary>
    public static void ToggleLinePrefix(TextEditor editor, string prefix)
    {
        var document = editor.Document;
        var selection = editor.TextArea.Selection;

        int firstLine = editor.TextArea.Caret.Line;
        int lastLine = firstLine;
        if (!selection.IsEmpty)
        {
            firstLine = selection.StartPosition.Line;
            lastLine = selection.EndPosition.Line;
            if (firstLine > lastLine) (firstLine, lastLine) = (lastLine, firstLine);
        }

        using (document.RunUpdate())
        {
            for (int number = firstLine; number <= lastLine; number++)
            {
                var line = document.GetLineByNumber(number);
                string text = document.GetText(line);
                int indent = 0;
                while (indent < text.Length && (text[indent] == ' ' || text[indent] == '\t')) indent++;

                string rest = text[indent..];
                if (rest.StartsWith(prefix, StringComparison.Ordinal))
                    document.Remove(line.Offset + indent, prefix.Length);
                else
                    document.Insert(line.Offset + indent, prefix);
            }
        }
    }

    public static void SetHeadingLevel(TextEditor editor, int level)
    {
        var document = editor.Document;
        var line = document.GetLineByNumber(editor.TextArea.Caret.Line);
        string text = document.GetText(line);

        int indent = 0;
        while (indent < text.Length && (text[indent] == ' ' || text[indent] == '\t')) indent++;

        int hashes = 0;
        while (indent + hashes < text.Length && text[indent + hashes] == '#') hashes++;

        int existing = hashes;
        if (existing > 0)
        {
            int after = indent + hashes;
            while (after < text.Length && (text[after] == ' ' || text[after] == '\t')) after++;
            document.Remove(line.Offset + indent, after - indent);
        }

        // Pressing the same level again turns the heading back into a paragraph.
        if (existing != level && level > 0)
            document.Insert(line.Offset + indent, new string('#', level) + " ");
    }

    public static void InsertLink(TextEditor editor)
    {
        var document = editor.Document;
        var (start, length) = GetTargetSegment(editor);
        string label = document.GetText(start, length);

        using (document.RunUpdate())
        {
            document.Replace(start, length, $"[{label}]()");
        }

        editor.CaretOffset = start + label.Length + 3;
        editor.TextArea.ClearSelection();
    }

    /// <summary>Ticks or unticks the task box on the caret's line, adding one if the line is a plain list item.</summary>
    public static void ToggleTask(TextEditor editor)
    {
        var document = editor.Document;
        var line = document.GetLineByNumber(editor.TextArea.Caret.Line);
        string text = document.GetText(line);

        var match = ListPrefixRegex.Match(text);
        if (!match.Groups["bullet"].Success && !match.Groups["number"].Success) return;

        var task = match.Groups["task"];
        if (task.Success)
        {
            bool done = task.Value.Contains('x') || task.Value.Contains('X');
            document.Replace(line.Offset + task.Index, 3, done ? "[ ]" : "[x]");
        }
        else
        {
            int insertAt = match.Groups["number"].Success
                ? match.Groups["number"].Index + match.Groups["number"].Length + 1
                : match.Groups["bullet"].Index + 1;
            while (insertAt < text.Length && (text[insertAt] == ' ' || text[insertAt] == '\t')) insertAt++;
            document.Insert(line.Offset + insertAt, "[ ] ");
        }
    }

    public static void DuplicateLine(TextEditor editor)
    {
        var document = editor.Document;
        var line = document.GetLineByNumber(editor.TextArea.Caret.Line);
        string text = document.GetText(line);

        int column = editor.CaretOffset - line.Offset;
        document.Insert(line.EndOffset, Environment.NewLine + text);
        editor.CaretOffset = Math.Min(document.TextLength, line.EndOffset + Environment.NewLine.Length + column);
    }

    public static void DeleteLine(TextEditor editor)
    {
        var document = editor.Document;
        var line = document.GetLineByNumber(editor.TextArea.Caret.Line);
        document.Remove(line.Offset, line.TotalLength > 0 ? line.TotalLength : line.Length);
    }

    /// <summary>Swaps the caret's line with the one above (-1) or below (+1), keeping the caret on it.</summary>
    public static void MoveLine(TextEditor editor, int direction)
    {
        var document = editor.Document;
        int number = editor.TextArea.Caret.Line;
        int target = number + direction;
        if (target < 1 || target > document.LineCount) return;

        var line = document.GetLineByNumber(number);
        var other = document.GetLineByNumber(target);
        int column = editor.CaretOffset - line.Offset;

        string lineText = document.GetText(line);
        string otherText = document.GetText(other);

        using (document.RunUpdate())
        {
            if (direction < 0)
            {
                document.Replace(other.Offset, other.Length, lineText);
                document.Replace(
                    document.GetLineByNumber(number).Offset,
                    document.GetLineByNumber(number).Length,
                    otherText);
            }
            else
            {
                document.Replace(line.Offset, line.Length, otherText);
                document.Replace(
                    document.GetLineByNumber(target).Offset,
                    document.GetLineByNumber(target).Length,
                    lineText);
            }
        }

        var moved = document.GetLineByNumber(target);
        editor.CaretOffset = Math.Min(moved.Offset + column, moved.EndOffset);
    }

    /// <summary>Opens an empty fenced code block below the caret and puts the caret inside it.</summary>
    public static void InsertCodeFence(TextEditor editor)
    {
        string newLine = Environment.NewLine;
        InsertBlock(editor, $"```{newLine}{newLine}```", caretOffsetIntoBlock: 3 + newLine.Length);
    }

    public static void InsertRule(TextEditor editor)
    {
        string newLine = Environment.NewLine;
        InsertBlock(editor, $"---{newLine}", caretOffsetIntoBlock: 3 + newLine.Length);
    }

    /// <summary>Inserts a block on its own lines, leaving the caret where the user will keep typing.</summary>
    private static void InsertBlock(TextEditor editor, string block, int caretOffsetIntoBlock)
    {
        var document = editor.Document;
        var line = document.GetLineByNumber(editor.TextArea.Caret.Line);

        string prefix = line.Length == 0 ? string.Empty : Environment.NewLine;
        int offset = line.EndOffset;

        document.Insert(offset, prefix + block);
        editor.TextArea.ClearSelection();
        editor.CaretOffset = Math.Min(document.TextLength, offset + prefix.Length + caretOffsetIntoBlock);
    }

    private static (int Start, int Length) GetTargetSegment(TextEditor editor)
    {
        var selection = editor.TextArea.Selection;
        if (!selection.IsEmpty)
        {
            var segment = selection.SurroundingSegment;
            return (segment.Offset, segment.Length);
        }

        var document = editor.Document;
        int caret = editor.CaretOffset;
        int start = caret;
        int end = caret;

        while (start > 0 && IsWordCharacter(document.GetCharAt(start - 1))) start--;
        while (end < document.TextLength && IsWordCharacter(document.GetCharAt(end))) end++;

        return (start, end - start);
    }

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '\'';

    private static void Select(TextEditor editor, int start, int length)
    {
        editor.TextArea.ClearSelection();
        if (length > 0) editor.Select(start, length);
        else editor.CaretOffset = start;
    }
}

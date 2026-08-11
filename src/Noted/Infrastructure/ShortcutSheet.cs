namespace Noted.Infrastructure;

/// <summary>
/// The keyboard reference, written in markdown so Noted can render it with its own editor.
/// Keep this in sync with <c>MainWindow.BuildInputBindings</c>.
/// </summary>
public static class ShortcutSheet
{
    public const string Markdown =
        """
        # Keyboard shortcuts

        ## Files and tabs

        - `Ctrl+N` / `Ctrl+T` — new note
        - `Ctrl+O` — open
        - `Ctrl+S` — save
        - `Ctrl+Shift+S` — save as
        - `Ctrl+Alt+S` — save every modified note
        - `Ctrl+W` / `Ctrl+F4` — close note
        - `Ctrl+Shift+T` — reopen the note you just closed
        - `Ctrl+Shift+W` — close Noted
        - `Ctrl+Tab` / `Ctrl+Shift+Tab` — next / previous note
        - `Ctrl+PageDown` / `Ctrl+PageUp` — next / previous note
        - `Ctrl+1` … `Ctrl+8` — jump to note by position
        - `Ctrl+9` — jump to the last note

        ## Search and navigation

        - `Ctrl+F` — find
        - `Ctrl+H` — find and replace
        - `F3` / `Shift+F3` — next / previous match
        - `Ctrl+G` — go to line
        - `Esc` — close the find bar

        ## Inline formatting

        - `Ctrl+B` — **bold**
        - `Ctrl+I` — *italic*
        - `Ctrl+E` — `inline code`
        - `Ctrl+Shift+H` — ==highlight==
        - `Ctrl+Shift+X` — ~~strikethrough~~
        - `Ctrl+K` — link

        ## Block formatting

        - `Ctrl+Alt+1` … `Ctrl+Alt+6` — heading level
        - `Ctrl+Alt+0` — back to paragraph
        - `Ctrl+Alt+L` — bullet list
        - `Ctrl+Shift+O` — numbered list
        - `Ctrl+Shift+C` — task checkbox
        - `Ctrl+Shift+Q` — blockquote
        - `Ctrl+Shift+M` — code block
        - `Ctrl+Shift+R` — horizontal rule

        ## Editing lines

        - `Ctrl+D` — duplicate line
        - `Ctrl+Shift+K` — delete line
        - `Alt+Up` / `Alt+Down` — move line up / down
        - `Enter` on an empty list item — end the list
        - `Ctrl+Z` / `Ctrl+Y` — undo / redo

        ## View

        - `Ctrl+Shift+L` — switch between dark and light
        - `Ctrl+Shift+P` — toggle live markdown (show all syntax)
        - `F11` — full screen
        - `Ctrl+=` / `Ctrl+-` — bigger / smaller text
        - `Ctrl+0` — reset text size
        - `Ctrl+,` — open settings
        - `Ctrl+Shift+,` — open the settings folder
        - `F1` — this sheet

        ---

        > This note is a scratch buffer. Edit it, close it, or save it somewhere —
        > `F1` always brings a fresh copy back.
        """;
}

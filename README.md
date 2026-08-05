# Noted

A lightweight markdown notepad for Windows. It behaves like Notepad — open a file, type,
save — but markdown is *live*: syntax characters are hidden on every line except the one
you're editing, the way Obsidian does it.

Put the caret on a line and its raw markdown appears. Move away and it renders again.
The document itself is never transformed — what you save is exactly what you typed.

```
## Heading          →   Heading            (## appears when the caret is on the line)
**bold** text       →   bold text
- [x] a task        →   ☑ a task
> quoted            →   ▏quoted
```

## Running it

```bash
dotnet run --project src/Noted
```

Requires the .NET 10 Windows desktop runtime. `Noted.exe path/to/file.md` opens a file
directly, and files can be dropped onto the window.

## What it does

- **Live markdown** — headings, bold, italic, strikethrough, inline code, highlights,
  links, images, blockquotes (nested), bullet and numbered lists, task checkboxes,
  horizontal rules, and fenced code blocks
- **Tabs** with unsaved indicators, reopen-closed, and session restore
- **Dark and light themes**, switched with `Ctrl+Shift+D`
- **A centred reading column** that keeps line length comfortable on wide monitors
- **Find** (`Ctrl+F`), **replace all** (`Ctrl+H`), **go to line** (`Ctrl+G`)
- **Smart lists** — `Enter` continues a list, quote or task; `Enter` on an empty item ends it
- Word and character count, caret position, and encoding in the status bar
- UTF‑8 by default, with BOM and UTF‑16 files detected on open and preserved on save

Press `F1` in the app for the full keyboard reference — it opens as a note, rendered by
the editor itself.

## How the live preview works

The interesting part is that there is no preview pane. The editor is
[AvalonEdit](https://github.com/icsharpcode/AvalonEdit) showing the real document, with
three pieces layered on top:

| Piece | Job |
| --- | --- |
| [`MarkdownScanner`](src/Noted/Markdown/MarkdownScanner.cs) | Tokenises one line at a time into content spans and marker spans |
| [`MarkdownAnalyzer`](src/Noted/Markdown/MarkdownAnalyzer.cs) | Caches those results and tracks the one cross-line concern: fenced code blocks |
| [`MarkdownColorizer`](src/Noted/Rendering/MarkdownColorizer.cs) | Applies fonts, weights and colours to the spans |
| [`MarkdownElementGenerator`](src/Noted/Rendering/MarkdownElementGenerator.cs) | Collapses marker spans to zero width, and swaps `-` for `•` and `[x]` for `☑` |
| [`BlockDecorationRenderer`](src/Noted/Rendering/BlockDecorationRenderer.cs) | Draws what isn't text: code panels, quote bars, rules |
| [`RevealTracker`](src/Noted/Rendering/RevealTracker.cs) | Decides which lines show their syntax — the caret's line, plus any selection |

Hiding is done with a zero-width `TextEmbeddedObject`
([`VisualElements.cs`](src/Noted/Rendering/VisualElements.cs)) rather than by editing text,
so undo, search, save and column positions all behave as if nothing were hidden.

The scanner is deliberately not a CommonMark parser. It only needs to know where syntax
characters are, which lets it stay line-local and cheap enough to run on every repaint.

## Layout

```
src/Noted/
  Markdown/      line tokeniser and per-document cache
  Rendering/     colorizer, element generator, decorations, themes
  Editing/       list continuation, formatting toggles, line operations
  Models/        open document (text, path, encoding, dirty state)
  Services/      settings persisted to %APPDATA%\Noted\settings.json
  Infrastructure/  small shared bits — prompt window, shortcut sheet
tests/Noted.Tests/  scanner and analyzer coverage
```

## Tests

```bash
dotnet test
```

Covers the markdown tokeniser and the fenced-code tracker — the parts where a subtle
mistake would quietly render the wrong thing.

## Settings

`%APPDATA%\Noted\settings.json`, or `Ctrl+,` to open the folder. Editable while the app is
closed; useful keys are `FontFamily`, `MonospaceFontFamily`, `FontSize`, `ReadingWidth`,
`Theme`, `WordWrap` and `LiveMarkdown`.

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

Fenced code blocks work the same way one level up: the whole block — not just the current
line — pops open when the caret is anywhere inside it. Step out, and the fence lines
collapse while a small language tag floats in the top-right corner of the block.

## Running it

```bash
dotnet run --project src/Noted
```

Requires the .NET 10 Windows desktop runtime. `Noted.exe path/to/file.md` opens a file
directly, and files can be dropped onto the window.

### Installer

```powershell
build\build-installer.ps1
```

Publishes the app and packages it into `build\Noted-Setup.msi` with
[WiX](https://wixtoolset.org/) — Start Menu and Desktop shortcuts, an Add/Remove Programs
entry, and the app icon throughout. On a new machine the script installs the WiX CLI
itself (as a global dotnet tool) the first time it's run.

Installing the MSI over an existing install automatically removes the previous version
first (via the installer's `MajorUpgrade` rule), so re-running the installer is how you
update Noted — no manual uninstall step needed.

The installer is framework-dependent (not self-contained), so it needs the .NET 10
Windows desktop runtime on the target machine — if it's missing, the generated
`Noted.exe` shows the standard .NET "install the runtime" prompt on launch.

## What it does

- **Live markdown** — headings, bold, italic, strikethrough, inline code, highlights,
  links, images, blockquotes (nested), bullet and numbered lists, task checkboxes,
  horizontal rules, and fenced code blocks
- **Tabs** with unsaved indicators, reopen-closed, and session restore — closing an
  untitled tab compacts the remaining "Untitled N" numbers instead of counting up forever
- **Dark and light themes**, switched with `Ctrl+Shift+D`, plus a full **Settings** window
  (`Ctrl+,`) for customizing colours, per-level heading colours and underline, fonts,
  reading-column margins, interface spacing, and a subtle grain texture — see below
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
| [`BlockDecorationRenderer`](src/Noted/Rendering/BlockDecorationRenderer.cs) | Draws what isn't text: code panels, quote bars, rules, language tags |
| [`RevealTracker`](src/Noted/Rendering/RevealTracker.cs) | Decides what's revealed — a line's syntax by caret/selection, or a whole fenced block by range |

Hiding is done with a zero-width `TextEmbeddedObject`
([`VisualElements.cs`](src/Noted/Rendering/VisualElements.cs)) rather than by editing text,
so undo, search, save and column positions all behave as if nothing were hidden.

The scanner is deliberately not a CommonMark parser. It only needs to know where syntax
characters are, which lets it stay line-local and cheap enough to run on every repaint.

## Layout

```
src/Noted/
  Markdown/      line tokeniser and per-document cache
  Rendering/     colorizer, element generator, decorations, themes, grain texture
  Editing/       list continuation, formatting toggles, line operations
  Models/        open document (text, path, encoding, dirty state)
  Services/      settings persisted to %APPDATA%\Noted\settings.json
  Settings/      the tabbed Settings window
  Infrastructure/  small shared bits — prompt window, shortcut sheet
  Assets/        app icon and theme-adaptive title-bar mark
tests/Noted.Tests/  scanner and analyzer coverage
assets/          source logo/wordmark exports
build/           installer sources, make-ico.ps1, and the build script
```

## Tests

```bash
dotnet test
```

Covers the markdown tokeniser and the fenced-code tracker — the parts where a subtle
mistake would quietly render the wrong thing.

## Settings

`Ctrl+,` (or the menu's "Settings…") opens a tabbed window that edits everything live —
no restart, no file to hand-edit:

| Tab | Covers |
| --- | --- |
| Appearance | Dark/light theme, UI and monospace font, font size |
| Colors | Hex overrides for background, surface, text, accent, links, code, quotes and rule lines — clear a field to fall back to the theme default |
| Headings | Underline toggle, plus a colour override per level (h1..h6) |
| Layout | Reading-column width and margins, and an interface-spacing multiplier for the title bar, tab strip and status bar |
| Effects | The grain texture overlay |

Everything is backed by `%APPDATA%\Noted\settings.json` (`Ctrl+Shift+,` opens the
folder directly), so it's still just a JSON file if you'd rather script it.

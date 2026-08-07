using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Search;
using Microsoft.Win32;
using Noted.Editing;
using Noted.Infrastructure;
using Noted.Markdown;
using Noted.Models;
using Noted.Rendering;
using Noted.Services;

namespace Noted;

public partial class MainWindow : Window
{
    private const string FileFilter =
        "Markdown & text (*.md;*.markdown;*.mdown;*.mkd;*.txt)|*.md;*.markdown;*.mdown;*.mkd;*.txt|" +
        "Markdown (*.md;*.markdown;*.mdown;*.mkd)|*.md;*.markdown;*.mdown;*.mkd|" +
        "Text files (*.txt)|*.txt|" +
        "All files (*.*)|*.*";

    private readonly ObservableCollection<NoteDocument> _documents = [];
    private readonly MarkdownAnalyzer _analyzer = new();
    private readonly RevealTracker _reveal = new();
    private readonly MarkdownColorizer _colorizer;
    private readonly MarkdownElementGenerator _generator;
    private readonly EmojiElementGenerator _emoji;
    private readonly InlineMathElementGenerator _inlineMath;
    private readonly BlockMathElementGenerator _blockMath;
    private readonly ImageElementGenerator _images = new();
    private BlockCollapser _collapser = null!;
    private readonly BlockDecorationRenderer _decorations;
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly AppSettings _settings;
    private readonly Stack<ClosedDocument> _closed = new();

    private SearchPanel? _searchPanel;
    private Settings.SettingsWindow? _settingsWindow;
    private NoteDocument? _shortcutSheet;
    private NoteDocument? _active;
    private bool _switchingTabs;
    private bool _resizingMargin;
    private bool _fullScreen;
    private WindowState _preFullScreenState = WindowState.Normal;
    private Rect _preFullScreenBounds;

    private readonly record struct ClosedDocument(string? FilePath, string Text);

    public MainWindow(IReadOnlyList<string> arguments)
    {
        _settings = App.Current.Settings;

        InitializeComponent();

        _colorizer = new MarkdownColorizer(_analyzer, _reveal);
        _generator = new MarkdownElementGenerator(_analyzer, _reveal);
        _emoji = new EmojiElementGenerator(_analyzer, _reveal);
        _inlineMath = new InlineMathElementGenerator(_analyzer, _reveal);
        _blockMath = new BlockMathElementGenerator(_analyzer, _reveal);
        _decorations = new BlockDecorationRenderer(_analyzer, _reveal);

        _statusTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(220),
        };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); UpdateStatusBar(); };

        _autoSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1500),
        };
        _autoSaveTimer.Tick += (_, _) => { _autoSaveTimer.Stop(); AutoSaveActiveDocument(); };

        TabList.ItemsSource = _documents;

        SetUpEditor();
        RestoreWindowPlacement();
        ApplySettingsToEditor();
        BuildInputBindings();

        OpenInitialDocuments(arguments);

        Loaded += (_, _) => { UpdateReadingWidth(); Editor.Focus(); };
        StateChanged += (_, _) => UpdateMaximizeState();
    }

    // ================= setup =================

    private void SetUpEditor()
    {
        var textView = Editor.TextArea.TextView;
        textView.LineTransformers.Add(_colorizer);
        // Images first: they claim the whole ![](…) span before the marker generator can pick at it.
        _images.RequestRedraw = () => Editor.TextArea.TextView.Redraw(DispatcherPriority.Render);
        textView.ElementGenerators.Add(_images);
        // Block math collapses whole $$…$$ ranges, so it must claim the span before line-local generators.
        textView.ElementGenerators.Add(_blockMath);
        textView.ElementGenerators.Add(_generator);
        textView.ElementGenerators.Add(_emoji);
        textView.ElementGenerators.Add(_inlineMath);
        textView.BackgroundRenderers.Add(_decorations);

        _collapser = new BlockCollapser(textView, [_blockMath]);

        DataObject.AddPastingHandler(Editor, OnEditorPaste);

        LeftMarginGrip.DragStarted += (_, _) => _resizingMargin = true;
        RightMarginGrip.DragStarted += (_, _) => _resizingMargin = true;
        LeftMarginGrip.DragDelta += (_, e) => ResizeReadingColumn(-2 * e.HorizontalChange);
        RightMarginGrip.DragDelta += (_, e) => ResizeReadingColumn(2 * e.HorizontalChange);
        LeftMarginGrip.DragCompleted += (_, _) => EndMarginResize();
        RightMarginGrip.DragCompleted += (_, _) => EndMarginResize();

        Editor.TextArea.TextView.Options.EnableHyperlinks = false;
        Editor.TextArea.TextView.Options.EnableEmailHyperlinks = false;
        Editor.TextArea.TextView.Options.AllowScrollBelowDocument = true;
        Editor.TextArea.TextView.Options.EnableRectangularSelection = true;
        Editor.TextArea.TextView.Options.HighlightCurrentLine = false;
        Editor.TextArea.Caret.CaretBrush = null;
        Editor.Options.IndentationSize = 4;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.CutCopyWholeLine = true;

        _reveal.Attach(Editor);
        _reveal.RevealChanged += RedrawLines;

        Editor.TextArea.Caret.PositionChanged += (_, _) => ScheduleStatusUpdate();
        Editor.TextChanged += OnEditorTextChanged;
        Editor.PreviewKeyDown += OnEditorPreviewKeyDown;
        Editor.PreviewMouseWheel += OnEditorPreviewMouseWheel;
        textView.MouseMove += OnTextViewMouseMove;
        textView.PreviewMouseLeftButtonDown += OnTextViewMouseLeftButtonDown;

        _searchPanel = SearchPanel.Install(Editor);
        _searchPanel.MarkerBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xF5, 0xC2, 0x42));

        Drop += OnFilesDropped;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
    }

    private void BuildInputBindings()
    {
        const ModifierKeys ctrl = ModifierKeys.Control;
        const ModifierKeys ctrlShift = ModifierKeys.Control | ModifierKeys.Shift;
        const ModifierKeys ctrlAlt = ModifierKeys.Control | ModifierKeys.Alt;

        // ---- files and tabs ----
        Bind(Key.N, ctrl, NewDocument);
        Bind(Key.T, ctrl, NewDocument);
        Bind(Key.T, ctrlShift, ReopenClosedDocument);
        Bind(Key.O, ctrl, OpenDocuments);
        Bind(Key.S, ctrl, () => SaveDocument(_active, saveAs: false));
        Bind(Key.S, ctrlShift, () => SaveDocument(_active, saveAs: true));
        Bind(Key.S, ctrlAlt, SaveAllDocuments);
        Bind(Key.W, ctrl, () => CloseDocument(_active));
        Bind(Key.F4, ctrl, () => CloseDocument(_active));
        Bind(Key.W, ctrlShift, Close);
        Bind(Key.Tab, ctrl, () => CycleTab(1));
        Bind(Key.Tab, ctrlShift, () => CycleTab(-1));
        Bind(Key.PageDown, ctrl, () => CycleTab(1));
        Bind(Key.PageUp, ctrl, () => CycleTab(-1));
        for (int slot = 1; slot <= 8; slot++)
        {
            int index = slot - 1;
            Bind(Key.D0 + slot, ctrl, () => SelectTab(index));
        }
        Bind(Key.D9, ctrl, () => SelectTab(_documents.Count - 1));

        // ---- search and navigation ----
        Bind(Key.F, ctrl, () => OpenSearchPanel(replace: false));
        Bind(Key.H, ctrl, () => OpenSearchPanel(replace: true));
        Bind(Key.G, ctrl, GoToLine);

        // ---- inline formatting ----
        Bind(Key.B, ctrl, () => MarkdownEditing.ToggleInline(Editor, "**"));
        Bind(Key.I, ctrl, () => MarkdownEditing.ToggleInline(Editor, "*"));
        Bind(Key.E, ctrl, () => MarkdownEditing.ToggleInline(Editor, "`"));
        Bind(Key.H, ctrlShift, () => MarkdownEditing.ToggleInline(Editor, "=="));
        Bind(Key.X, ctrlShift, () => MarkdownEditing.ToggleInline(Editor, "~~"));
        Bind(Key.K, ctrl, () => MarkdownEditing.InsertLink(Editor));

        // ---- block formatting ----
        Bind(Key.Q, ctrlShift, () => MarkdownEditing.ToggleLinePrefix(Editor, "> "));
        Bind(Key.L, ctrlShift, () => MarkdownEditing.ToggleLinePrefix(Editor, "- "));
        Bind(Key.O, ctrlShift, () => MarkdownEditing.ToggleLinePrefix(Editor, "1. "));
        Bind(Key.C, ctrlShift, () => MarkdownEditing.ToggleTask(Editor));
        Bind(Key.M, ctrlShift, () => MarkdownEditing.InsertCodeFence(Editor));
        Bind(Key.R, ctrlShift, () => MarkdownEditing.InsertRule(Editor));

        for (int level = 1; level <= 6; level++)
        {
            int captured = level;
            Bind(Key.D0 + level, ctrlAlt, () => MarkdownEditing.SetHeadingLevel(Editor, captured));
        }
        Bind(Key.D0, ctrlAlt, () => MarkdownEditing.SetHeadingLevel(Editor, 0));

        // ---- line surgery ----
        Bind(Key.D, ctrl, () => MarkdownEditing.DuplicateLine(Editor));
        Bind(Key.K, ctrlShift, () => MarkdownEditing.DeleteLine(Editor));
        Bind(Key.Up, ModifierKeys.Alt, () => MarkdownEditing.MoveLine(Editor, -1));
        Bind(Key.Down, ModifierKeys.Alt, () => MarkdownEditing.MoveLine(Editor, 1));

        // ---- view ----
        Bind(Key.D, ctrlShift, ToggleTheme);
        Bind(Key.F11, ModifierKeys.None, ToggleFullScreen);
        Bind(Key.OemPlus, ctrl, () => Zoom(1));
        Bind(Key.Add, ctrl, () => Zoom(1));
        Bind(Key.OemMinus, ctrl, () => Zoom(-1));
        Bind(Key.Subtract, ctrl, () => Zoom(-1));
        Bind(Key.D0, ctrl, () => SetFontSize(AppSettings.DefaultFontSize));
        Bind(Key.NumPad0, ctrl, () => SetFontSize(AppSettings.DefaultFontSize));
        Bind(Key.P, ctrlShift, ToggleLiveMarkdown);
        Bind(Key.OemComma, ctrl, OpenSettings);
        Bind(Key.OemComma, ctrlShift, OpenSettingsFolder);
        Bind(Key.F1, ModifierKeys.None, ShowShortcutSheet);

        void Bind(Key key, ModifierKeys modifiers, Action action) =>
            InputBindings.Add(new KeyBinding(new RelayCommand(action), key, modifiers));
    }

    private void OpenInitialDocuments(IReadOnlyList<string> arguments)
    {
        var paths = arguments.Where(a => !a.StartsWith('-')).Where(File.Exists).ToList();
        if (paths.Count == 0) paths = _settings.OpenFiles.Where(File.Exists).ToList();

        foreach (string path in paths) OpenPath(path, focus: false);

        RestoreDrafts();

        if (_documents.Count == 0) NewDocument();
        TabList.SelectedIndex = 0;
    }

    /// <summary>Reopens untitled notes that were autosaved to the drafts cache on a previous run.</summary>
    private void RestoreDrafts()
    {
        if (!Directory.Exists(AppSettings.DraftsDirectoryPath)) return;

        IEnumerable<string> draftPaths;
        try
        {
            draftPaths = Directory.EnumerateFiles(AppSettings.DraftsDirectoryPath, "*.md").ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        bool restoredAny = false;
        foreach (string draftPath in draftPaths)
        {
            try
            {
                _documents.Add(NoteDocument.LoadDraft(draftPath));
                restoredAny = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        if (restoredAny) RenumberUntitledDocuments();
    }

    // ================= documents =================

    private void NewDocument()
    {
        var note = NoteDocument.CreateEmpty();
        _documents.Add(note);
        RenumberUntitledDocuments();
        TabList.SelectedItem = note;
        Editor.Focus();
    }

    /// <summary>Keeps "Untitled N" compact: closing Untitled 2 relabels Untitled 3 down to 2, and the
    /// next new tab reuses the smallest free slot instead of a counter that only ever grows.</summary>
    private void RenumberUntitledDocuments()
    {
        int number = 1;
        foreach (var doc in _documents)
        {
            if (doc.FilePath is null) doc.SetUntitledNumber(number++);
        }
    }

    private void OpenDocuments()
    {
        var dialog = new OpenFileDialog { Filter = FileFilter, Multiselect = true, Title = "Open" };
        if (dialog.ShowDialog(this) != true) return;

        foreach (string path in dialog.FileNames) OpenPath(path, focus: true);
    }

    private void OpenPath(string path, bool focus)
    {
        string full = Path.GetFullPath(path);

        var existing = _documents.FirstOrDefault(
            d => string.Equals(d.FilePath, full, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            TabList.SelectedItem = existing;
            return;
        }

        NoteDocument note;
        try
        {
            note = NoteDocument.Load(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            MessageBox.Show(this, ex.Message, "Couldn't open file", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Replace a pristine, unused first tab rather than stacking an empty note beside the file.
        if (_documents.Count == 1 && _documents[0] is { FilePath: null, IsModified: false } blank &&
            blank.Document.TextLength == 0)
        {
            _documents.Clear();
        }

        _documents.Add(note);
        RenumberUntitledDocuments();
        if (focus) TabList.SelectedItem = note;
    }

    private bool SaveDocument(NoteDocument? note, bool saveAs)
    {
        if (note is null) return false;

        string? path = note.FilePath;
        if (saveAs || path is null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = FileFilter,
                Title = "Save as",
                DefaultExt = ".md",
                FileName = path is null ? note.Title + ".md" : Path.GetFileName(path),
                InitialDirectory = path is null ? null : Path.GetDirectoryName(path),
            };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }

        try
        {
            note.Save(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Couldn't save file", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        RenumberUntitledDocuments();
        UpdateStatusBar();
        ShowSavedIndicator();
        return true;
    }

    private void SaveAllDocuments()
    {
        foreach (var note in _documents.Where(d => d.IsModified).ToList())
        {
            if (!SaveDocument(note, saveAs: false)) return;
        }
    }

    private void CloseDocument(NoteDocument? note)
    {
        if (note is null) return;
        if (!ConfirmClose(note)) return;

        // Only a real save keeps a document around; an untitled note's secret draft cache dies with its tab.
        if (note.FilePath is null) note.DeleteDraft();

        if (note.FilePath is not null || note.Document.TextLength > 0)
            _closed.Push(new ClosedDocument(note.FilePath, note.Document.Text));

        int index = _documents.IndexOf(note);
        _documents.Remove(note);
        RenumberUntitledDocuments();

        if (_documents.Count == 0)
        {
            NewDocument();
            return;
        }

        TabList.SelectedIndex = Math.Clamp(index, 0, _documents.Count - 1);
        Editor.Focus();
    }

    private bool ConfirmClose(NoteDocument note)
    {
        if (ReferenceEquals(note, _active)) FlushAutoSave();
        if (!note.IsModified) return true;

        TabList.SelectedItem = note;
        var result = MessageBox.Show(
            this,
            $"Save changes to {note.Title}?",
            "Noted",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => SaveDocument(note, saveAs: false),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private void ActivateDocument(NoteDocument? note)
    {
        if (ReferenceEquals(note, _active)) return;

        if (_active is not null)
        {
            FlushAutoSave();
            _active.CaretOffset = Editor.CaretOffset;
            _active.ScrollOffset = Editor.VerticalOffset;
        }

        _switchingTabs = true;
        _active = note;

        _collapser?.Clear();   // drop folds tied to the old document before swapping it out
        Editor.Document = note?.Document;
        _analyzer.Attach(note?.Document);

        if (note is not null)
        {
            Editor.CaretOffset = Math.Clamp(note.CaretOffset, 0, note.Document.TextLength);
            Editor.ScrollToVerticalOffset(note.ScrollOffset);
        }

        _switchingTabs = false;

        _collapser?.Update();
        Editor.TextArea.TextView.Redraw();
        UpdateStatusBar();
        Title = note is null ? "Noted" : $"{note.Title} — Noted";
    }

    private void ReopenClosedDocument()
    {
        while (_closed.Count > 0)
        {
            var entry = _closed.Pop();

            if (entry.FilePath is not null && File.Exists(entry.FilePath))
            {
                OpenPath(entry.FilePath, focus: true);
                return;
            }

            if (entry.FilePath is null)
            {
                var note = NoteDocument.CreateWithText(entry.Text);
                _documents.Add(note);
                RenumberUntitledDocuments();
                TabList.SelectedItem = note;
                Editor.Focus();
                return;
            }
        }
    }

    private void CycleTab(int delta)
    {
        if (_documents.Count < 2) return;
        int index = (TabList.SelectedIndex + delta + _documents.Count) % _documents.Count;
        TabList.SelectedIndex = index;
    }

    private void SelectTab(int index)
    {
        if (index >= 0 && index < _documents.Count) TabList.SelectedIndex = index;
    }

    private void OpenSearchPanel(bool replace)
    {
        if (replace)
        {
            ReplaceAll();
            return;
        }

        _searchPanel ??= SearchPanel.Install(Editor);
        if (!Editor.TextArea.Selection.IsEmpty)
            _searchPanel.SearchPattern = Editor.TextArea.Selection.GetText();

        _searchPanel.Open();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _searchPanel.Reactivate());
    }

    /// <summary>
    /// AvalonEdit's search panel is find-only, so replace is its own prompt: it swaps every
    /// occurrence in one undoable step and reports how many it touched.
    /// </summary>
    private void ReplaceAll()
    {
        var document = Editor.Document;
        if (document is null) return;

        string initial = Editor.TextArea.Selection.IsEmpty
            ? string.Empty
            : Editor.TextArea.Selection.GetText();

        var answer = PromptWindow.AskPair(this, "Replace all", "Replace with", initial);
        if (answer is not { } pair || pair.First.Length == 0) return;

        // Collect against one snapshot, then rewrite back-to-front so earlier offsets stay valid.
        string text = document.Text;
        var offsets = new List<int>();
        for (int i = text.IndexOf(pair.First, StringComparison.Ordinal);
             i >= 0;
             i = text.IndexOf(pair.First, i + pair.First.Length, StringComparison.Ordinal))
        {
            offsets.Add(i);
        }

        using (document.RunUpdate())
        {
            for (int i = offsets.Count - 1; i >= 0; i--)
                document.Replace(offsets[i], pair.First.Length, pair.Second);
        }

        StatusPath.Text = offsets.Count == 0
            ? $"No matches for “{pair.First}”"
            : $"Replaced {offsets.Count:N0} occurrence{(offsets.Count == 1 ? "" : "s")}";
    }

    private void GoToLine()
    {
        if (Editor.Document is null) return;

        string? answer = PromptWindow.Ask(
            this,
            $"Go to line (1–{Editor.Document.LineCount})",
            Editor.TextArea.Caret.Line.ToString());

        if (!int.TryParse(answer, out int line)) return;

        line = Math.Clamp(line, 1, Editor.Document.LineCount);
        Editor.CaretOffset = Editor.Document.GetLineByNumber(line).Offset;
        Editor.ScrollToLine(line);
        Editor.Focus();
    }

    private void ToggleFullScreen()
    {
        _fullScreen = !_fullScreen;

        if (_fullScreen) EnterFullScreen();
        else ExitFullScreen();

        TitleBar.Visibility = _fullScreen ? Visibility.Collapsed : Visibility.Visible;
        StatusBar.Visibility = _fullScreen ? Visibility.Collapsed : Visibility.Visible;
        UpdateMaximizeState();
    }

    /// <summary>
    /// A genuine full screen: <see cref="WindowState.Maximized"/> only fills the work area and leaves
    /// the taskbar showing, so instead we sit the window at the monitor's real pixel bounds and make it
    /// topmost, which covers the taskbar too.
    /// </summary>
    private void EnterFullScreen()
    {
        _preFullScreenState = WindowState;
        _preFullScreenBounds = new Rect(Left, Top, Width, Height);

        // Drop out of Maximized first; a maximized window snaps back to the work area no matter what
        // rect we ask for.
        WindowState = WindowState.Normal;

        var bounds = GetCurrentMonitorBounds();
        Topmost = true;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    private void ExitFullScreen()
    {
        Topmost = false;

        if (_preFullScreenState == WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal;
            Left = _preFullScreenBounds.Left;
            Top = _preFullScreenBounds.Top;
            Width = _preFullScreenBounds.Width;
            Height = _preFullScreenBounds.Height;
        }
    }

    /// <summary>The full (not work-area) bounds of the monitor the window currently sits on, in DIPs.</summary>
    private Rect GetCurrentMonitorBounds()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            return new Rect(Left, Top, Width, Height);

        var rc = info.rcMonitor;
        var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is { } target)
        {
            var toDip = target.TransformFromDevice;
            var topLeft = toDip.Transform(new Point(rc.Left, rc.Top));
            var bottomRight = toDip.Transform(new Point(rc.Right, rc.Bottom));
            return new Rect(topLeft, bottomRight);
        }

        return new Rect(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
    }

    private void ToggleLiveMarkdown()
    {
        _settings.LiveMarkdown = !_settings.LiveMarkdown;
        ApplySettingsToEditor();
        Editor.TextArea.TextView.Redraw();
    }

    /// <summary>Opens the shortcut reference as a note — it is markdown, so the editor renders it.</summary>
    private void ShowShortcutSheet()
    {
        if (_shortcutSheet is not null && _documents.Contains(_shortcutSheet))
        {
            TabList.SelectedItem = _shortcutSheet;
            return;
        }

        var note = NoteDocument.CreateWithText(ShortcutSheet.Markdown);
        _shortcutSheet = note;
        _documents.Add(note);
        RenumberUntitledDocuments();
        TabList.SelectedItem = note;
        Editor.Focus();
    }

    // ================= editor behaviour =================

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_switchingTabs) return;

        // Editing can add, remove or resize a $$…$$ / table / diagram block, so re-sync the folds.
        _collapser.Update();

        ScheduleStatusUpdate();
        ScheduleAutoSave();
        if (_active is not null) Title = $"{_active.Title} — Noted";
    }

    private void OnEditorPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return &&
            e.KeyboardDevice.Modifiers == ModifierKeys.None &&
            MarkdownEditing.TryContinueList(Editor))
        {
            e.Handled = true;
        }
    }

    private void OnEditorPreviewMouseWheel(object? sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        Zoom(Math.Sign(e.Delta));
        e.Handled = true;
    }

    /// <summary>
    /// Pasting an image drops it into the shared image cache and inserts a markdown reference to it,
    /// instead of letting the editor paste nothing (or a bitmap it can't hold as text).
    /// </summary>
    private void OnEditorPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Bitmap)) return;
        if (Clipboard.GetImage() is not { } image) return;

        string? path = SavePastedImage(image);
        if (path is null) return;

        Editor.Document.Insert(Editor.CaretOffset, $"![]({path})");
        e.CancelCommand();
        StatusPath.Text = "Pasted image";
    }

    /// <summary>Writes a pasted bitmap to <c>%APPDATA%\Noted\images</c> as PNG and returns its path.</summary>
    private static string? SavePastedImage(System.Windows.Media.Imaging.BitmapSource image)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.ImagesDirectoryPath);
            string path = Path.Combine(AppSettings.ImagesDirectoryPath, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png");

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var stream = File.Create(path);
            encoder.Save(stream);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A hand cursor over a code-block's language tag (copies it) or a link (Ctrl+click opens it).</summary>
    private void OnTextViewMouseMove(object sender, MouseEventArgs e)
    {
        var textView = Editor.TextArea.TextView;
        bool interactive = _decorations.TryHitLanguageTag(e.GetPosition(textView), out _, out _)
            || LinkUrlAt(e.GetPosition(Editor)) is not null;
        textView.Cursor = interactive ? Cursors.Hand : Cursors.IBeam;
    }

    private void OnTextViewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var textView = Editor.TextArea.TextView;
        if (_decorations.TryHitLanguageTag(e.GetPosition(textView), out int start, out int end))
        {
            CopyCodeBlock(start, end);
            e.Handled = true;
            return;
        }

        // Ctrl+click opens a link; a plain click keeps placing the caret so link text stays editable.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && LinkUrlAt(e.GetPosition(Editor)) is { } url)
        {
            OpenLink(url);
            e.Handled = true;
        }
    }

    /// <summary>The target URL of a markdown link under a point in editor coordinates, or null.</summary>
    private string? LinkUrlAt(Point editorPoint)
    {
        var document = Editor.Document;
        if (document is null) return null;

        var position = Editor.GetPositionFromPoint(editorPoint);
        if (position is not { } pos) return null;

        int offset = document.GetOffset(pos.Location);
        var docLine = document.GetLineByOffset(offset);
        int rel = offset - docLine.Offset;

        // Walk the line's tokens tracking the most recent link opener; when the closing "](url)"
        // marker turns up, the span from opener to url end is the whole link.
        var info = _analyzer.GetLine(docLine.LineNumber);
        int openStart = -1;
        foreach (var token in info.Tokens)
        {
            bool isLink = (token.Style & MdStyle.Link) != 0;
            bool isUrl = (token.Style & MdStyle.Url) != 0;

            if (token.IsMarker && isLink && !isUrl) openStart = token.Offset;

            if (isUrl && openStart >= 0 && rel >= openStart && rel < token.End)
            {
                string raw = document.GetText(docLine.Offset + token.Offset, token.Length);
                return ExtractUrl(raw);
            }
        }

        return null;
    }

    /// <summary>Pulls the address out of a "](https://…)" url marker.</summary>
    private static string? ExtractUrl(string marker)
    {
        int open = marker.IndexOf('(');
        int close = marker.LastIndexOf(')');
        if (open < 0 || close <= open) return null;

        string url = marker[(open + 1)..close].Trim();
        return url.Length == 0 ? null : url;
    }

    private void OpenLink(string url)
    {
        // Only hand well-formed web/mail links to the shell; never launch an arbitrary local path.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https" or "mailto"))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
        }
    }

    /// <summary>Copies the code inside a fenced block (the lines between its delimiters) to the clipboard.</summary>
    private void CopyCodeBlock(int startLine, int endLine)
    {
        var document = Editor.Document;
        if (document is null) return;

        var lines = new List<string>();
        for (int n = startLine + 1; n <= endLine - 1 && n <= document.LineCount; n++)
        {
            var line = document.GetLineByNumber(n);
            lines.Add(document.GetText(line.Offset, line.Length));
        }

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return; // clipboard busy — nothing we can do, don't crash
        }

        StatusPath.Text = $"Copied {lines.Count} line{(lines.Count == 1 ? "" : "s")} of code";
    }

    private void RedrawLines(int fromLine, int toLine)
    {
        var document = Editor.Document;
        if (document is null) return;

        // A caret crossing a block edge changes whether that block is folded; sync before repainting.
        _collapser.Update();

        // A caret crossing into or out of a fenced code block changes how the whole block
        // looks (its fence lines, its language tag), not just the line the caret landed on.
        if (_analyzer.TryGetCodeBlock(fromLine, out int s1, out int e1, out _))
        {
            fromLine = Math.Min(fromLine, s1);
            toLine = Math.Max(toLine, e1);
        }
        if (_analyzer.TryGetCodeBlock(toLine, out int s2, out int e2, out _))
        {
            fromLine = Math.Min(fromLine, s2);
            toLine = Math.Max(toLine, e2);
        }

        // Collapsing blocks (display math, and later tables/diagrams) add or remove whole visual
        // lines when the caret crosses their edge, so a per-line redraw can't reconcile the layout —
        // repaint everything in that case.
        if (_analyzer.TryGetMathBlock(fromLine, out _, out _) || _analyzer.TryGetMathBlock(toLine, out _, out _))
        {
            Editor.TextArea.TextView.Redraw(DispatcherPriority.Render);
            return;
        }

        var textView = Editor.TextArea.TextView;
        if (toLine - fromLine > 200)
        {
            textView.Redraw(DispatcherPriority.Render);
            return;
        }

        for (int number = Math.Max(1, fromLine); number <= Math.Min(toLine, document.LineCount); number++)
            textView.Redraw(document.GetLineByNumber(number), DispatcherPriority.Render);
    }

    private void Zoom(int direction) => SetFontSize(Editor.FontSize + direction * 1.0);

    private void SetFontSize(double size)
    {
        Editor.FontSize = Math.Clamp(size, 9, 40);
        _settings.FontSize = Editor.FontSize;
        Editor.TextArea.TextView.Redraw();
        UpdateStatusBar();
    }

    private void ApplySettingsToEditor()
    {
        var theme = EditorTheme.Resolve(_settings.Theme, _settings);

        _colorizer.Theme = theme;
        _colorizer.MonospaceFont = new FontFamily(_settings.MonospaceFontFamily);
        _generator.Theme = theme;
        _generator.HideMarkers = _settings.LiveMarkdown;
        _emoji.HideMarkers = _settings.LiveMarkdown;
        _inlineMath.HideMarkers = _settings.LiveMarkdown;
        _inlineMath.Theme = theme;
        _blockMath.HideMarkers = _settings.LiveMarkdown;
        _blockMath.Theme = theme;
        _images.HideMarkers = _settings.LiveMarkdown;
        _reveal.Enabled = _settings.LiveMarkdown;
        _decorations.Theme = theme;
        _decorations.MonospaceFont = new FontFamily(_settings.MonospaceFontFamily);
        _decorations.HideMarkers = _settings.LiveMarkdown;

        AppMark.Source = new BitmapImage(new Uri(
            _settings.Theme == AppTheme.Dark
                ? "pack://application:,,,/Assets/icon-light.png"
                : "pack://application:,,,/Assets/icon-dark.png"));

        Editor.FontFamily = new FontFamily(_settings.FontFamily);
        Editor.FontSize = _settings.FontSize;
        Editor.WordWrap = _settings.WordWrap;
        Editor.ShowLineNumbers = _settings.ShowLineNumbers;
        Editor.Foreground = theme.Text;
        Editor.LineNumbersForeground = theme.Faint;
        Editor.TextArea.SelectionBrush = theme.Selection;
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.Caret.CaretBrush = theme.Accent;

        GrainOverlay.Visibility = _settings.GrainEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (_settings.GrainEnabled)
        {
            GrainOverlay.Background = GrainTexture.Brush;
            GrainOverlay.Opacity = _settings.GrainOpacity;
        }

        ApplyChromeSpacing();

        ThemeButton.Content = _settings.Theme == AppTheme.Dark ? "\uE706" : "\uE708";

        if (_settings.LiveMarkdown) _collapser?.Update();
        else _collapser?.Clear();

        Editor.TextArea.TextView.Redraw();
        UpdateReadingWidth();
        UpdateStatusBar();
    }

    private void ToggleTheme()
    {
        var next = _settings.Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        App.Current.ApplyTheme(next);
        ApplySettingsToEditor();
    }

    private void UpdateReadingWidth()
    {
        double available = EditorHost.ActualWidth;
        if (available <= 0) return;

        double side = Math.Max(_settings.MarginHorizontal, (available - _settings.ReadingWidth) / 2);
        double rightSide = side * 0.6;
        Editor.Padding = new Thickness(side, _settings.MarginTop, rightSide, _settings.MarginBottom);

        // Light "page" behind the text column; the darker EditorHost shows through as the margins.
        // The page extends a little past the text on both sides so letters keep a small breathing
        // strip of paper before the darker margin begins, instead of sitting flush against it.
        const double pageInset = 20;
        ReadingSurface.Margin = new Thickness(
            Math.Max(0, side - pageInset), 0, Math.Max(0, rightSide - pageInset), 0);

        _decorations.ContentWidth = Math.Max(120, available - side * 1.6);
        _blockMath.ContentWidth = Math.Max(120, available - side - rightSide);
        _images.MaxWidth = Math.Max(120, available - side - rightSide);
        Editor.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);

        // Park the drag grips on the page edges. Skip this mid-drag: moving a Thumb under the cursor
        // resets its drag origin and makes the resize jump.
        if (!_resizingMargin)
        {
            double gripHalf = LeftMarginGrip.Width / 2;
            LeftMarginGrip.Margin = new Thickness(Math.Max(0, side - pageInset) - gripHalf, 0, 0, 0);
            RightMarginGrip.Margin = new Thickness(available - Math.Max(0, rightSide - pageInset) - gripHalf, 0, 0, 0);
        }
    }

    /// <summary>Widens or narrows the reading column live as a margin grip is dragged.</summary>
    private void ResizeReadingColumn(double delta)
    {
        _settings.ReadingWidth = Math.Clamp(_settings.ReadingWidth + delta, 480, 1400);
        UpdateReadingWidth();
    }

    private void EndMarginResize()
    {
        _resizingMargin = false;
        UpdateReadingWidth();
    }

    /// <summary>Scales the title bar and status bar height by the "spacing" setting for a more/less airy shell.</summary>
    private void ApplyChromeSpacing()
    {
        double spacing = Math.Clamp(_settings.Spacing, 0.6, 2.0);
        TitleBarRow.Height = new GridLength(40 * spacing);
        StatusBarRow.Height = new GridLength(26 * spacing);
    }

    // ================= status bar =================

    private void ScheduleStatusUpdate()
    {
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    // ================= autosave =================

    private void ScheduleAutoSave()
    {
        if (!_settings.AutoSaveEnabled) return;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    /// <summary>Saves right now instead of waiting out the debounce — used before a tab/app close can ask "save changes?".</summary>
    private void FlushAutoSave()
    {
        if (!_autoSaveTimer.IsEnabled) return;
        _autoSaveTimer.Stop();
        AutoSaveActiveDocument();
    }

    private void AutoSaveActiveDocument()
    {
        if (!_settings.AutoSaveEnabled) return;

        var note = _active;
        if (note is null || !note.IsModified) return;

        try
        {
            if (note.FilePath is not null)
            {
                note.Save(note.FilePath);
            }
            else
            {
                Directory.CreateDirectory(AppSettings.DraftsDirectoryPath);
                note.SaveDraft(note.DraftPath ?? Path.Combine(AppSettings.DraftsDirectoryPath, $"{Guid.NewGuid():N}.md"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        RenumberUntitledDocuments();
        UpdateStatusBar();
        ShowSavedIndicator();
    }

    private void ShowSavedIndicator()
    {
        StatusSaved.Text = "Saved";
        StatusSaved.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.4)) { BeginTime = TimeSpan.FromSeconds(0.6) });
    }

    private void UpdateStatusBar()
    {
        if (_active is null) return;

        var document = _active.Document;
        var caret = Editor.TextArea.Caret;

        StatusPath.Text = _active.FilePath ?? (_active.DraftPath is null ? "Not saved yet" : "Draft — not saved to a file yet");
        StatusCaret.Text = $"Ln {caret.Line}, Col {caret.Column}";
        StatusEncoding.Text = DescribeEncoding(_active);

        int words = CountWords(document.Text);
        StatusStats.Text = $"{words:N0} words · {document.TextLength:N0} chars";
    }

    private static string DescribeEncoding(NoteDocument note) => note.Encoding.WebName.ToUpperInvariant();

    private static int CountWords(string text)
    {
        int count = 0;
        bool inWord = false;

        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c)) inWord = false;
            else if (!inWord) { inWord = true; count++; }
        }

        return count;
    }

    // ================= chrome + menu =================

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ActivateDocument(TabList.SelectedItem as NoteDocument);
    }

    private void OnNewTabClick(object sender, RoutedEventArgs e) => NewDocument();

    private void OnCloseTabClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NoteDocument note) CloseDocument(note);
    }

    private void OnEditorHostSizeChanged(object sender, SizeChangedEventArgs e) => UpdateReadingWidth();

    private void OnToggleThemeClick(object sender, RoutedEventArgs e) => ToggleTheme();

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void UpdateMaximizeState()
    {
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";

        // Full screen owns the whole monitor edge to edge \u2014 no compensating margin, no border.
        if (_fullScreen)
        {
            RootBorder.Margin = new Thickness(0);
            RootBorder.BorderThickness = new Thickness(0);
            return;
        }

        RootBorder.Margin = maximized ? new Thickness(7) : new Thickness(0);
        RootBorder.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
    }

    // ---- Win32: the true monitor bounds, taskbar included ----

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private void OnMenuClick(object sender, RoutedEventArgs e)
    {
        var menu = BuildMenu();
        menu.PlacementTarget = MenuButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { Style = (Style)FindResource("AppContextMenu") };

        Add("New note", "Ctrl+N", NewDocument);
        Add("Open…", "Ctrl+O", OpenDocuments);
        Add("Reopen closed note", "Ctrl+Shift+T", ReopenClosedDocument);
        Add("Save", "Ctrl+S", () => SaveDocument(_active, saveAs: false));
        Add("Save as…", "Ctrl+Shift+S", () => SaveDocument(_active, saveAs: true));
        Add("Save all", "Ctrl+Alt+S", SaveAllDocuments);
        Add("Close note", "Ctrl+W", () => CloseDocument(_active));
        Separator();

        Add("Find…", "Ctrl+F", () => OpenSearchPanel(replace: false));
        Add("Replace…", "Ctrl+H", () => OpenSearchPanel(replace: true));
        Add("Go to line…", "Ctrl+G", GoToLine);
        Separator();

        Add("Bold", "Ctrl+B", () => MarkdownEditing.ToggleInline(Editor, "**"));
        Add("Italic", "Ctrl+I", () => MarkdownEditing.ToggleInline(Editor, "*"));
        Add("Inline code", "Ctrl+E", () => MarkdownEditing.ToggleInline(Editor, "`"));
        Add("Highlight", "Ctrl+Shift+H", () => MarkdownEditing.ToggleInline(Editor, "=="));
        Add("Link", "Ctrl+K", () => MarkdownEditing.InsertLink(Editor));
        Add("Task checkbox", "Ctrl+Shift+C", () => MarkdownEditing.ToggleTask(Editor));
        Add("Code block", "Ctrl+Shift+M", () => MarkdownEditing.InsertCodeFence(Editor));
        Separator();

        Toggle("Live markdown", _settings.LiveMarkdown, _ => ToggleLiveMarkdown());
        Toggle("Word wrap", _settings.WordWrap, value => { _settings.WordWrap = value; ApplySettingsToEditor(); });
        Toggle("Line numbers", _settings.ShowLineNumbers,
            value => { _settings.ShowLineNumbers = value; ApplySettingsToEditor(); });
        Toggle("Light theme", _settings.Theme == AppTheme.Light, _ => ToggleTheme());
        Toggle("Full screen", _fullScreen, _ => ToggleFullScreen());
        Separator();

        Add("Keyboard shortcuts", "F1", ShowShortcutSheet);
        Add("Settings…", "Ctrl+,", OpenSettings);
        Add("Settings file…", "Ctrl+Shift+,", OpenSettingsFolder);

        return menu;

        void Add(string header, string? gesture, Action action)
        {
            var item = new MenuItem
            {
                Header = header,
                InputGestureText = gesture,
                Style = (Style)FindResource("AppMenuItem"),
            };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        void Toggle(string header, bool isChecked, Action<bool> action)
        {
            var item = new MenuItem
            {
                Header = header,
                IsCheckable = true,
                IsChecked = isChecked,
                Style = (Style)FindResource("AppMenuItem"),
            };
            item.Click += (_, _) => action(item.IsChecked);
            menu.Items.Add(item);
        }

        void Separator() => menu.Items.Add(new Separator { Style = (Style)FindResource("MenuSeparator") });
    }

    private void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new Settings.SettingsWindow(this, _settings, RefreshTheme) { Owner = this };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        _settingsWindow.Activate();
    }

    /// <summary>Re-resolves the theme and reapplies every editor setting; the live-update callback for the settings window.</summary>
    private void RefreshTheme()
    {
        App.Current.ApplyTheme(_settings.Theme);
        ApplySettingsToEditor();
    }

    private void OpenSettingsFolder()
    {
        _settings.Save();
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppSettings.DirectoryPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private void OnFilesDropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        foreach (string file in files.Where(File.Exists)) OpenPath(file, focus: true);
        e.Handled = true;
    }

    // ================= lifetime =================

    private void RestoreWindowPlacement()
    {
        Width = Math.Max(MinWidth, _settings.WindowWidth);
        Height = Math.Max(MinHeight, _settings.WindowHeight);
        if (_settings.WindowMaximized) WindowState = WindowState.Maximized;
        UpdateMaximizeState();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        foreach (var note in _documents.ToList())
        {
            if (!ConfirmClose(note))
            {
                e.Cancel = true;
                return;
            }
        }

        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }
        _settings.OpenFiles = _documents.Where(d => d.FilePath is not null).Select(d => d.FilePath!).ToList();
        _settings.Save();

        base.OnClosing(e);
    }
}

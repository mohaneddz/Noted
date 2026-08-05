using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        "Markdown (*.md;*.markdown;*.mdown;*.mkd)|*.md;*.markdown;*.mdown;*.mkd|" +
        "Text files (*.txt)|*.txt|All files (*.*)|*.*";

    private readonly ObservableCollection<NoteDocument> _documents = [];
    private readonly MarkdownAnalyzer _analyzer = new();
    private readonly RevealTracker _reveal = new();
    private readonly MarkdownColorizer _colorizer;
    private readonly MarkdownElementGenerator _generator;
    private readonly BlockDecorationRenderer _decorations;
    private readonly DispatcherTimer _statusTimer;
    private readonly AppSettings _settings;

    private NoteDocument? _active;
    private bool _switchingTabs;

    public MainWindow(IReadOnlyList<string> arguments)
    {
        _settings = App.Current.Settings;

        InitializeComponent();

        _colorizer = new MarkdownColorizer(_analyzer, _reveal);
        _generator = new MarkdownElementGenerator(_analyzer, _reveal);
        _decorations = new BlockDecorationRenderer(_analyzer);

        _statusTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(220),
        };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); UpdateStatusBar(); };

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
        textView.ElementGenerators.Add(_generator);
        textView.BackgroundRenderers.Add(_decorations);

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

        var searchPanel = SearchPanel.Install(Editor);
        searchPanel.MarkerBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xF5, 0xC2, 0x42));

        Drop += OnFilesDropped;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
    }

    private void BuildInputBindings()
    {
        Bind(Key.N, ModifierKeys.Control, NewDocument);
        Bind(Key.O, ModifierKeys.Control, OpenDocuments);
        Bind(Key.S, ModifierKeys.Control, () => SaveDocument(_active, saveAs: false));
        Bind(Key.S, ModifierKeys.Control | ModifierKeys.Shift, () => SaveDocument(_active, saveAs: true));
        Bind(Key.W, ModifierKeys.Control, () => CloseDocument(_active));
        Bind(Key.D, ModifierKeys.Control, ToggleTheme);
        Bind(Key.Tab, ModifierKeys.Control, () => CycleTab(1));
        Bind(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift, () => CycleTab(-1));

        Bind(Key.B, ModifierKeys.Control, () => MarkdownEditing.ToggleInline(Editor, "**"));
        Bind(Key.I, ModifierKeys.Control, () => MarkdownEditing.ToggleInline(Editor, "*"));
        Bind(Key.E, ModifierKeys.Control, () => MarkdownEditing.ToggleInline(Editor, "`"));
        Bind(Key.H, ModifierKeys.Control | ModifierKeys.Shift, () => MarkdownEditing.ToggleInline(Editor, "=="));
        Bind(Key.X, ModifierKeys.Control | ModifierKeys.Shift, () => MarkdownEditing.ToggleInline(Editor, "~~"));
        Bind(Key.K, ModifierKeys.Control, () => MarkdownEditing.InsertLink(Editor));
        Bind(Key.Q, ModifierKeys.Control | ModifierKeys.Shift, () => MarkdownEditing.ToggleLinePrefix(Editor, "> "));
        Bind(Key.L, ModifierKeys.Control | ModifierKeys.Shift, () => MarkdownEditing.ToggleLinePrefix(Editor, "- "));

        for (int level = 1; level <= 6; level++)
        {
            int captured = level;
            Bind(Key.D0 + level, ModifierKeys.Control | ModifierKeys.Alt,
                () => MarkdownEditing.SetHeadingLevel(Editor, captured));
        }
        Bind(Key.D0, ModifierKeys.Control | ModifierKeys.Alt, () => MarkdownEditing.SetHeadingLevel(Editor, 0));

        Bind(Key.OemPlus, ModifierKeys.Control, () => Zoom(1));
        Bind(Key.Add, ModifierKeys.Control, () => Zoom(1));
        Bind(Key.OemMinus, ModifierKeys.Control, () => Zoom(-1));
        Bind(Key.Subtract, ModifierKeys.Control, () => Zoom(-1));
        Bind(Key.D0, ModifierKeys.Control, () => SetFontSize(15.5));

        void Bind(Key key, ModifierKeys modifiers, Action action) =>
            InputBindings.Add(new KeyBinding(new RelayCommand(action), key, modifiers));
    }

    private void OpenInitialDocuments(IReadOnlyList<string> arguments)
    {
        var paths = arguments.Where(a => !a.StartsWith('-')).Where(File.Exists).ToList();
        if (paths.Count == 0) paths = _settings.OpenFiles.Where(File.Exists).ToList();

        foreach (string path in paths) OpenPath(path, focus: false);

        if (_documents.Count == 0) NewDocument();
        TabList.SelectedIndex = 0;
    }

    // ================= documents =================

    private void NewDocument()
    {
        var note = NoteDocument.CreateEmpty();
        _documents.Add(note);
        TabList.SelectedItem = note;
        Editor.Focus();
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

        UpdateStatusBar();
        return true;
    }

    private void CloseDocument(NoteDocument? note)
    {
        if (note is null) return;
        if (!ConfirmClose(note)) return;

        int index = _documents.IndexOf(note);
        _documents.Remove(note);

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
            _active.CaretOffset = Editor.CaretOffset;
            _active.ScrollOffset = Editor.VerticalOffset;
        }

        _switchingTabs = true;
        _active = note;

        Editor.Document = note?.Document;
        _analyzer.Attach(note?.Document);

        if (note is not null)
        {
            Editor.CaretOffset = Math.Clamp(note.CaretOffset, 0, note.Document.TextLength);
            Editor.ScrollToVerticalOffset(note.ScrollOffset);
        }

        _switchingTabs = false;

        Editor.TextArea.TextView.Redraw();
        UpdateStatusBar();
        Title = note is null ? "Noted" : $"{note.Title} — Noted";
    }

    private void CycleTab(int delta)
    {
        if (_documents.Count < 2) return;
        int index = (TabList.SelectedIndex + delta + _documents.Count) % _documents.Count;
        TabList.SelectedIndex = index;
    }

    // ================= editor behaviour =================

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_switchingTabs) return;

        ScheduleStatusUpdate();
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

    private void RedrawLines(int fromLine, int toLine)
    {
        var document = Editor.Document;
        if (document is null) return;

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
        var theme = EditorTheme.For(_settings.Theme);

        _colorizer.Theme = theme;
        _colorizer.MonospaceFont = new FontFamily(_settings.MonospaceFontFamily);
        _generator.Theme = theme;
        _generator.HideMarkers = _settings.LiveMarkdown;
        _reveal.Enabled = _settings.LiveMarkdown;
        _decorations.Theme = theme;

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

        ThemeButton.Content = _settings.Theme == AppTheme.Dark ? "\uE706" : "\uE708";

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

        double side = Math.Max(28, (available - _settings.ReadingWidth) / 2);
        Editor.Padding = new Thickness(side, 20, side * 0.6, 60);
    }

    // ================= status bar =================

    private void ScheduleStatusUpdate()
    {
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void UpdateStatusBar()
    {
        if (_active is null) return;

        var document = _active.Document;
        var caret = Editor.TextArea.Caret;

        StatusPath.Text = _active.FilePath ?? "Not saved yet";
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
        RootBorder.Margin = maximized ? new Thickness(7) : new Thickness(0);
        RootBorder.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
    }

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
        Add("Save", "Ctrl+S", () => SaveDocument(_active, saveAs: false));
        Add("Save as…", "Ctrl+Shift+S", () => SaveDocument(_active, saveAs: true));
        Add("Close note", "Ctrl+W", () => CloseDocument(_active));
        Separator();

        Add("Find / replace", "Ctrl+F", () => SearchPanel.Install(Editor).Open());
        Separator();

        Add("Bold", "Ctrl+B", () => MarkdownEditing.ToggleInline(Editor, "**"));
        Add("Italic", "Ctrl+I", () => MarkdownEditing.ToggleInline(Editor, "*"));
        Add("Inline code", "Ctrl+E", () => MarkdownEditing.ToggleInline(Editor, "`"));
        Add("Highlight", "Ctrl+Shift+H", () => MarkdownEditing.ToggleInline(Editor, "=="));
        Add("Link", "Ctrl+K", () => MarkdownEditing.InsertLink(Editor));
        Separator();

        Toggle("Live markdown", _settings.LiveMarkdown, value =>
        {
            _settings.LiveMarkdown = value;
            ApplySettingsToEditor();
            Editor.TextArea.TextView.Redraw();
        });
        Toggle("Word wrap", _settings.WordWrap, value => { _settings.WordWrap = value; ApplySettingsToEditor(); });
        Toggle("Line numbers", _settings.ShowLineNumbers,
            value => { _settings.ShowLineNumbers = value; ApplySettingsToEditor(); });
        Toggle("Light theme", _settings.Theme == AppTheme.Light, _ => ToggleTheme());
        Separator();

        Add("Settings file…", null, OpenSettingsFolder);

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

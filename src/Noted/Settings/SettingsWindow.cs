using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Noted.Rendering;
using Noted.Services;

namespace Noted.Settings;

/// <summary>
/// A live-updating preferences window: every control writes straight into the shared
/// <see cref="AppSettings"/> instance and calls back into the shell so changes are visible
/// immediately, the same way the "Light theme" menu toggle already behaves.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _onChange;
    private readonly Dictionary<string, FrameworkElement> _panels = new();
    private readonly Dictionary<string, Button> _tabButtons = new();

    public SettingsWindow(Window owner, AppSettings settings, Action onChange)
    {
        _settings = settings;
        _onChange = onChange;

        Owner = owner;
        Title = "Settings";
        Width = 640;
        Height = 640;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.ToolWindow;
        ShowInTaskbar = false;
        Background = Brush("Brush.Background", Brushes.Black);
        Foreground = Brush("Brush.Text", Brushes.White);
        FontFamily = Application.Current.TryFindResource("Font.Ui") as FontFamily ?? new FontFamily("Segoe UI");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(16, 14, 16, 6),
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var contentHost = new Grid { Margin = new Thickness(4, 0, 4, 0) };
        Grid.SetRow(contentHost, 1);
        root.Children.Add(contentHost);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 8, 16, 16),
        };
        Grid.SetRow(footer, 2);
        var close = new Button
        {
            Content = "Close",
            Style = (Style)FindResource("SettingsAccentButton"),
            IsDefault = true,
        };
        close.Click += (_, _) => Close();
        footer.Children.Add(close);
        root.Children.Add(footer);

        AddTab(header, contentHost, "Appearance", BuildAppearanceTab());
        AddTab(header, contentHost, "Colors", BuildColorsTab());
        AddTab(header, contentHost, "Headings", BuildHeadingsTab());
        AddTab(header, contentHost, "Layout", BuildLayoutTab());
        AddTab(header, contentHost, "Effects", BuildEffectsTab());
        SelectTab("Appearance");

        Content = root;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    // ================= tab plumbing =================

    private void AddTab(StackPanel header, Grid contentHost, string name, UIElement content)
    {
        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(12, 4, 20, 12),
            Visibility = Visibility.Collapsed,
        };
        contentHost.Children.Add(scroll);
        _panels[name] = scroll;

        var button = new Button
        {
            Content = name,
            Style = (Style)FindResource("SettingsTabButton"),
            Margin = new Thickness(0, 0, 4, 0),
        };
        button.Click += (_, _) => SelectTab(name);
        header.Children.Add(button);
        _tabButtons[name] = button;
    }

    private void SelectTab(string name)
    {
        foreach (var (key, panel) in _panels)
            panel.Visibility = key == name ? Visibility.Visible : Visibility.Collapsed;

        foreach (var (key, button) in _tabButtons)
        {
            bool active = key == name;
            button.Background = active ? Brush("Brush.SurfaceAlt", Brushes.Gray) : Brushes.Transparent;
            button.Foreground = active ? Brush("Brush.Text", Brushes.White) : Brush("Brush.Muted", Brushes.Gray);
        }
    }

    // ================= tabs =================

    private UIElement BuildAppearanceTab()
    {
        var panel = new StackPanel();

        panel.Children.Add(Header("Theme"));
        var themeRow = new StackPanel { Orientation = Orientation.Horizontal };
        var dark = new RadioButton { Content = "Dark", GroupName = "theme", Style = (Style)FindResource("SettingsCheckBox"), Margin = new Thickness(0, 0, 16, 0) };
        var light = new RadioButton { Content = "Light", GroupName = "theme", Style = (Style)FindResource("SettingsCheckBox") };
        dark.IsChecked = _settings.Theme == AppTheme.Dark;
        light.IsChecked = _settings.Theme == AppTheme.Light;
        dark.Checked += (_, _) => { _settings.Theme = AppTheme.Dark; _onChange(); };
        light.Checked += (_, _) => { _settings.Theme = AppTheme.Light; _onChange(); };
        themeRow.Children.Add(dark);
        themeRow.Children.Add(light);
        panel.Children.Add(themeRow);

        panel.Children.Add(Header("Fonts"));
        panel.Children.Add(TextRow("Text font", _settings.FontFamily, value => { _settings.FontFamily = value; _onChange(); }));
        panel.Children.Add(TextRow("Monospace font", _settings.MonospaceFontFamily, value => { _settings.MonospaceFontFamily = value; _onChange(); }));
        panel.Children.Add(SliderRow("Font size", _settings.FontSize, 9, 32, 0.5,
            value => { _settings.FontSize = value; _onChange(); }));

        return panel;
    }

    private UIElement BuildColorsTab() => ColorsPanel(
        "Overrides the active theme's palette. Clear a field to fall back to the theme default.",
        ("Background", () => _settings.Colors.Background, v => _settings.Colors.Background = v),
        ("Surface", () => _settings.Colors.Surface, v => _settings.Colors.Surface = v),
        ("Text", () => _settings.Colors.Text, v => _settings.Colors.Text = v),
        ("Muted text", () => _settings.Colors.Muted, v => _settings.Colors.Muted = v),
        ("Accent", () => _settings.Colors.Accent, v => _settings.Colors.Accent = v),
        ("Links", () => _settings.Colors.Link, v => _settings.Colors.Link = v),
        ("Inline code", () => _settings.Colors.Code, v => _settings.Colors.Code = v),
        ("Quotes", () => _settings.Colors.Quote, v => _settings.Colors.Quote = v),
        ("Rule lines", () => _settings.Colors.RuleLine, v => _settings.Colors.RuleLine = v));

    private UIElement BuildHeadingsTab()
    {
        var panel = new StackPanel();

        panel.Children.Add(Header("Style"));
        var underline = new CheckBox
        {
            Content = "Underline headings",
            Style = (Style)FindResource("SettingsCheckBox"),
            IsChecked = _settings.HeadingUnderline,
        };
        underline.Checked += (_, _) => { _settings.HeadingUnderline = true; _onChange(); };
        underline.Unchecked += (_, _) => { _settings.HeadingUnderline = false; _onChange(); };
        panel.Children.Add(underline);

        panel.Children.Add(Header("Colors"));
        panel.Children.Add(Hint("Per-level colours for h1 through h6. An empty field falls back to \"Default heading colour\" below."));
        panel.Children.Add(ColorRow("Default heading colour", () => _settings.Colors.Heading, v => _settings.Colors.Heading = v));

        while (_settings.HeadingColors.Count < 6) _settings.HeadingColors.Add(null);
        for (int i = 0; i < 6; i++)
        {
            int level = i;
            panel.Children.Add(ColorRow($"h{level + 1}",
                () => _settings.HeadingColors[level],
                v => _settings.HeadingColors[level] = v));
        }

        return panel;
    }

    private UIElement BuildLayoutTab()
    {
        var panel = new StackPanel();

        panel.Children.Add(Header("Reading column"));
        panel.Children.Add(SliderRow("Width", _settings.ReadingWidth, 480, 1400, 10,
            value => { _settings.ReadingWidth = value; _onChange(); }));
        panel.Children.Add(SliderRow("Side margin", _settings.MarginHorizontal, 12, 160, 2,
            value => { _settings.MarginHorizontal = value; _onChange(); }));
        panel.Children.Add(SliderRow("Top margin", _settings.MarginTop, 0, 120, 2,
            value => { _settings.MarginTop = value; _onChange(); }));
        panel.Children.Add(SliderRow("Bottom margin", _settings.MarginBottom, 0, 160, 2,
            value => { _settings.MarginBottom = value; _onChange(); }));

        panel.Children.Add(Header("Interface"));
        panel.Children.Add(Hint("Scales the title bar, tab strip and status bar height."));
        panel.Children.Add(SliderRow("Spacing", _settings.Spacing, 0.6, 2.0, 0.05,
            value => { _settings.Spacing = value; _onChange(); }));

        return panel;
    }

    private UIElement BuildEffectsTab()
    {
        var panel = new StackPanel();

        panel.Children.Add(Header("Grain"));
        panel.Children.Add(Hint("Overlays a faint static noise texture on the editor surface."));
        var grain = new CheckBox
        {
            Content = "Enable grain texture",
            Style = (Style)FindResource("SettingsCheckBox"),
            Margin = new Thickness(0, 6, 0, 0),
            IsChecked = _settings.GrainEnabled,
        };
        grain.Checked += (_, _) => { _settings.GrainEnabled = true; _onChange(); };
        grain.Unchecked += (_, _) => { _settings.GrainEnabled = false; _onChange(); };
        panel.Children.Add(grain);

        return panel;
    }

    // ================= row builders =================

    private UIElement ColorsPanel(string hint, params (string Label, Func<string?> Get, Action<string?> Set)[] rows)
    {
        var panel = new StackPanel();
        panel.Children.Add(Hint(hint));
        foreach (var (label, get, set) in rows)
            panel.Children.Add(ColorRow(label, get, set));

        var reset = new Button
        {
            Content = "Reset all colours",
            Style = (Style)FindResource("SettingsGhostButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 0),
        };
        reset.Click += (_, _) =>
        {
            _settings.Colors = new ThemeColorOverrides();
            _onChange();
            SelectTab("Colors");
            RebuildTab("Colors", BuildColorsTab());
        };
        panel.Children.Add(reset);
        return panel;
    }

    private void RebuildTab(string name, UIElement content)
    {
        if (_panels.TryGetValue(name, out var scroll) && scroll is ScrollViewer viewer)
            viewer.Content = content;
    }

    private FrameworkElement Header(string text) => new TextBlock
    {
        Text = text,
        Style = (Style)FindResource("SettingsSectionHeader"),
    };

    private FrameworkElement Hint(string text) => new TextBlock
    {
        Text = text,
        Style = (Style)FindResource("SettingsHint"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8),
    };

    private FrameworkElement TextRow(string label, string initialValue, Action<string> onCommit)
    {
        var grid = LabeledRow(label, out var slot);

        var box = new TextBox
        {
            Text = initialValue,
            Style = (Style)FindResource("SettingsTextBox"),
            Width = 260,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        box.LostFocus += (_, _) => onCommit(box.Text);
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { onCommit(box.Text); Keyboard.ClearFocus(); } };
        slot.Children.Add(box);
        return grid;
    }

    private FrameworkElement SliderRow(string label, double initialValue, double min, double max, double step, Action<double> onChange)
    {
        var grid = LabeledRow(label, out var slot);

        var value = new TextBlock
        {
            Style = (Style)FindResource("SettingsHint"),
            Width = 44,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(8, 0, 0, 0),
            Text = initialValue.ToString("0.##", CultureInfo.InvariantCulture),
        };

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            SmallChange = step,
            Value = initialValue,
            Width = 220,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("Brush.Accent", Brushes.Purple),
        };
        slider.ValueChanged += (_, e) =>
        {
            value.Text = e.NewValue.ToString("0.##", CultureInfo.InvariantCulture);
            onChange(e.NewValue);
        };

        slot.Children.Add(slider);
        slot.Children.Add(value);
        return grid;
    }

    private FrameworkElement ColorRow(string label, Func<string?> get, Action<string?> set)
    {
        var grid = LabeledRow(label, out var slot);

        var swatch = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("Brush.Border", Brushes.Gray),
            Margin = new Thickness(0, 0, 8, 0),
            Background = SwatchBrush(get()),
        };

        var box = new TextBox
        {
            Text = get() ?? string.Empty,
            Style = (Style)FindResource("SettingsTextBox"),
            Width = 110,
        };

        var clear = new Button
        {
            Content = "✕",
            Style = (Style)FindResource("SettingsGhostButton"),
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "Reset to theme default",
        };

        void Commit(string? hex)
        {
            set(string.IsNullOrWhiteSpace(hex) ? null : hex.Trim());
            swatch.Background = SwatchBrush(get());
            _onChange();
        }

        box.LostFocus += (_, _) => Commit(box.Text);
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(box.Text); Keyboard.ClearFocus(); } };
        clear.Click += (_, _) => { box.Text = string.Empty; Commit(null); };

        slot.Children.Add(swatch);
        slot.Children.Add(box);
        slot.Children.Add(clear);
        return grid;
    }

    private static Brush SwatchBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Brushes.Transparent;
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        }
        catch (FormatException)
        {
            return Brushes.Transparent;
        }
    }

    private Grid LabeledRow(string label, out StackPanel slot)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock { Text = label, Style = (Style)FindResource("SettingsLabel") };
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        slot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(slot, 1);
        grid.Children.Add(slot);

        return grid;
    }

    private static Brush Brush(string key, Brush fallback) =>
        Application.Current.TryFindResource(key) as Brush ?? fallback;
}

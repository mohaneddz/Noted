using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
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
        Width = 760;
        Height = 620;
        MinWidth = 640;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        ShowInTaskbar = false;
        this.SetResourceReference(BackgroundProperty, "Brush.Background");
        this.SetResourceReference(ForegroundProperty, "Brush.Text");
        FontFamily = Application.Current.TryFindResource("Font.Ui") as FontFamily ?? new FontFamily("Segoe UI");

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        var outer = new Border { BorderThickness = new Thickness(1) };
        outer.SetResourceReference(Border.BackgroundProperty, "Brush.Background");
        outer.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.Child = root;

        var titleBar = new Grid { Height = 40 };
        titleBar.SetResourceReference(Panel.BackgroundProperty, "Brush.Surface");
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.MouseLeftButtonDown += (_, e) => { if (e.ChangedButton == MouseButton.Left) DragMove(); };

        var titleText = new TextBlock
        {
            Text = "Settings",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        Grid.SetColumn(titleText, 0);
        titleBar.Children.Add(titleText);

        var closeButton = new Button { Content = "", Style = (Style)FindResource("CloseButton") };
        closeButton.Click += (_, _) => Close();
        Grid.SetColumn(closeButton, 1);
        titleBar.Children.Add(closeButton);

        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var sidebar = new Border { BorderThickness = new Thickness(0, 0, 1, 0) };
        sidebar.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        sidebar.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var nav = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
        sidebar.Child = nav;
        Grid.SetColumn(sidebar, 0);
        body.Children.Add(sidebar);

        var contentHost = new Grid();
        Grid.SetColumn(contentHost, 1);
        body.Children.Add(contentHost);

        AddTab(nav, contentHost, "General", BuildGeneralTab());
        AddTab(nav, contentHost, "Appearance", BuildAppearanceTab());
        AddTab(nav, contentHost, "Colors", BuildColorsTab());
        AddTab(nav, contentHost, "Headings", BuildHeadingsTab());
        AddTab(nav, contentHost, "Layout", BuildLayoutTab());
        AddTab(nav, contentHost, "Effects", BuildEffectsTab());
        SelectTab("General");

        Content = outer;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    // ================= tab plumbing =================

    private void AddTab(StackPanel nav, Grid contentHost, string name, UIElement content)
    {
        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(28, 24, 28, 24),
            Visibility = Visibility.Collapsed,
        };
        // ScrollViewer content isn't reliably width-constrained by HorizontalScrollBarVisibility=Disabled
        // alone once WindowChrome is in play, so pin it to the viewport explicitly.
        if (content is FrameworkElement contentElement)
        {
            scroll.SizeChanged += (_, e) =>
                contentElement.Width = Math.Max(0, e.NewSize.Width - scroll.Padding.Left - scroll.Padding.Right);
        }
        contentHost.Children.Add(scroll);
        _panels[name] = scroll;

        var button = new Button
        {
            Content = name,
            Style = (Style)FindResource("SettingsNavItem"),
        };
        button.Click += (_, _) => SelectTab(name);
        nav.Children.Add(button);
        _tabButtons[name] = button;
    }

    private void SelectTab(string name)
    {
        foreach (var (key, panel) in _panels)
            panel.Visibility = key == name ? Visibility.Visible : Visibility.Collapsed;

        foreach (var (key, button) in _tabButtons)
        {
            bool active = key == name;
            if (active) button.SetResourceReference(Control.BackgroundProperty, "Brush.SurfaceAlt");
            else button.Background = Brushes.Transparent;
            button.SetResourceReference(Control.ForegroundProperty, active ? "Brush.Text" : "Brush.Muted");
        }
    }

    // ================= tabs =================

    private UIElement BuildGeneralTab()
    {
        var panel = new StackPanel();

        panel.Children.Add(Header("Saving"));
        panel.Children.Add(ToggleRow("Autosave", "Saves the active note automatically about 1.5 seconds after you stop typing. "
            + "Notes you haven't saved anywhere yet are cached privately and reopen next time you launch Noted — "
            + "closing their tab without saving discards the cache.",
            _settings.AutoSaveEnabled, v => { _settings.AutoSaveEnabled = v; _onChange(); }));

        return panel;
    }

    private UIElement BuildAppearanceTab()
    {
        var panel = new StackPanel();

        panel.Children.Add(Header("Theme"));
        var themeRow = new StackPanel { Orientation = Orientation.Horizontal };
        var dark = new RadioButton { Content = "Dark", GroupName = "theme", Style = (Style)FindResource("SettingsRadioButton"), Margin = new Thickness(0, 0, 16, 0) };
        var light = new RadioButton { Content = "Light", GroupName = "theme", Style = (Style)FindResource("SettingsRadioButton") };
        dark.IsChecked = _settings.Theme == AppTheme.Dark;
        light.IsChecked = _settings.Theme == AppTheme.Light;
        dark.Checked += (_, _) => { _settings.Theme = AppTheme.Dark; _onChange(); };
        light.Checked += (_, _) => { _settings.Theme = AppTheme.Light; _onChange(); };
        themeRow.Children.Add(dark);
        themeRow.Children.Add(light);
        panel.Children.Add(themeRow);

        panel.Children.Add(Header("Fonts"));
        panel.Children.Add(FontRow("Text font", _settings.FontFamily, value => { _settings.FontFamily = value; _onChange(); }));
        panel.Children.Add(FontRow("Monospace font", _settings.MonospaceFontFamily, value => { _settings.MonospaceFontFamily = value; _onChange(); }));
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
        panel.Children.Add(ToggleRow("Underline headings", null, _settings.HeadingUnderline,
            v => { _settings.HeadingUnderline = v; _onChange(); }));

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
        panel.Children.Add(Hint("Scales the title bar, tab strip and status bar height — not the text."));
        panel.Children.Add(SliderRow("Interface scale", _settings.Spacing, 0.6, 2.0, 0.05,
            value => { _settings.Spacing = value; _onChange(); }));

        return panel;
    }

    private UIElement BuildEffectsTab()
    {
        var panel = new StackPanel();

        panel.Children.Add(Header("Grain"));
        panel.Children.Add(ToggleRow("Enable grain texture", "Overlays a faint static noise texture on the editor surface.",
            _settings.GrainEnabled, v => { _settings.GrainEnabled = v; _onChange(); }));
        panel.Children.Add(SliderRow("Intensity", _settings.GrainOpacity, 0.01, 0.25, 0.01,
            value => { _settings.GrainOpacity = value; _onChange(); }));

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

    private static FontFamily[]? _systemFonts;

    /// <summary>A dropdown of the installed system font families, each previewed in its own typeface.
    /// Nothing is bundled with the app — the list is whatever Windows already has.</summary>
    private FrameworkElement FontRow(string label, string current, Action<string> onCommit)
    {
        var grid = LabeledRow(label, out var slot);

        _systemFonts ??= System.Windows.Media.Fonts.SystemFontFamilies
            .OrderBy(f => f.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var combo = new ComboBox
        {
            Style = (Style)FindResource("SettingsComboBox"),
            Width = 260,
            MaxDropDownHeight = 340,
            ItemsSource = _systemFonts,
        };

        // Render each family name in its own face so the list reads like a real font menu.
        var itemStyle = new Style(typeof(ComboBoxItem));
        itemStyle.Setters.Add(new Setter(Control.FontFamilyProperty, new System.Windows.Data.Binding()));
        itemStyle.Setters.Add(new Setter(Control.FontSizeProperty, 13.5));
        combo.ItemContainerStyle = itemStyle;

        // The stored value can be a fallback list ("Cascadia Mono, Consolas, …"); pick the first
        // token that is actually installed so the box reflects what the editor really renders with.
        var tokens = current.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();
        combo.SelectedItem = _systemFonts.FirstOrDefault(
            f => tokens.Any(t => string.Equals(f.Source, t, StringComparison.OrdinalIgnoreCase)));

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is FontFamily family) onCommit(family.Source);
        };

        slot.Children.Add(combo);
        return grid;
    }

    private FrameworkElement SliderRow(string label, double initialValue, double min, double max, double step, Action<double> onChange)
    {
        var grid = LabeledRow(label, out var slot);

        // A typed value box paired with the slider: dragging updates the box, and typing a valid
        // number inside the range drives the slider — both preview instantly.
        var value = new TextBox
        {
            Style = (Style)FindResource("SettingsTextBox"),
            Width = 58,
            Margin = new Thickness(8, 0, 0, 0),
            TextAlignment = TextAlignment.Right,
            Text = initialValue.ToString("0.##", CultureInfo.InvariantCulture),
        };

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            SmallChange = step,
            Value = initialValue,
            Width = 200,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
        };
        slider.SetResourceReference(Control.ForegroundProperty, "Brush.Accent");

        bool syncing = false;

        slider.ValueChanged += (_, e) =>
        {
            if (syncing) return;
            syncing = true;
            value.Text = e.NewValue.ToString("0.##", CultureInfo.InvariantCulture);
            syncing = false;
            onChange(e.NewValue);
        };

        void ApplyTyped()
        {
            if (syncing) return;
            if (double.TryParse(value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double typed)
                && typed >= min && typed <= max)
            {
                syncing = true;
                slider.Value = typed;
                syncing = false;
                value.ClearValue(Control.ForegroundProperty);
                onChange(typed);
            }
            else
            {
                // Leave the slider where it is, but flag the entry as out of range / unparseable.
                value.Foreground = Brush("Brush.Accent", Brushes.OrangeRed);
            }
        }

        value.TextChanged += (_, _) => ApplyTyped();
        value.LostFocus += (_, _) =>
        {
            // Snap a rejected entry back to the slider's real value when focus leaves.
            if (!double.TryParse(value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double typed)
                || typed < min || typed > max)
            {
                value.ClearValue(Control.ForegroundProperty);
                value.Text = slider.Value.ToString("0.##", CultureInfo.InvariantCulture);
            }
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
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 0),
            Background = SwatchBrush(get()),
            Cursor = Cursors.Hand,
            ToolTip = "Pick a colour",
        };
        swatch.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");

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

        bool syncing = false;

        // Apply a value the moment it's valid, so the editor previews as you type or drag the picker.
        // An empty field means "reset to the theme default"; a half-typed/invalid hex is flagged and
        // simply left unapplied until it parses.
        void Preview()
        {
            if (syncing) return;
            string text = box.Text.Trim();
            if (text.Length == 0)
            {
                set(null);
                swatch.Background = Brushes.Transparent;
                box.ClearValue(Control.ForegroundProperty);
                _onChange();
            }
            else if (TryParseColor(text, out var color))
            {
                set(text);
                swatch.Background = new SolidColorBrush(color);
                box.ClearValue(Control.ForegroundProperty);
                _onChange();
            }
            else
            {
                box.Foreground = Brush("Brush.Accent", Brushes.OrangeRed);
            }
        }

        box.TextChanged += (_, _) => Preview();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };

        clear.Click += (_, _) => { box.Text = string.Empty; };

        swatch.MouseLeftButtonUp += (_, _) =>
        {
            using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AllowFullOpen = true };
            if (TryParseColor(box.Text, out var current))
                dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            var picked = dialog.Color;
            syncing = true;
            box.Text = $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}";
            box.ClearValue(Control.ForegroundProperty);
            syncing = false;
            Preview();
        };

        slot.Children.Add(swatch);
        slot.Children.Add(box);
        slot.Children.Add(clear);
        return grid;
    }

    private static Brush SwatchBrush(string? hex) =>
        TryParseColor(hex, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;

    private static bool TryParseColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try
        {
            if (ColorConverter.ConvertFromString(hex.Trim()) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException) { }
        catch (InvalidOperationException) { }
        return false;
    }

    private FrameworkElement ToggleRow(string label, string? hint, bool initialValue, Action<bool> onChange)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 8, 0, 8), LastChildFill = true };

        var toggle = new CheckBox
        {
            Style = (Style)FindResource("ToggleSwitch"),
            IsChecked = initialValue,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggle.Checked += (_, _) => onChange(true);
        toggle.Unchecked += (_, _) => onChange(false);
        DockPanel.SetDock(toggle, Dock.Right);
        dock.Children.Add(toggle);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) };
        text.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("SettingsLabel") });
        if (!string.IsNullOrEmpty(hint))
            text.Children.Add(new TextBlock
            {
                Text = hint,
                Style = (Style)FindResource("SettingsHint"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
        dock.Children.Add(text);

        return dock;
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

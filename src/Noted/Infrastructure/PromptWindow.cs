using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Noted.Infrastructure;

/// <summary>A one-line modal prompt, themed to match the shell. Used by Go to line.</summary>
public sealed class PromptWindow : Window
{
    private readonly TextBox _input;
    private readonly TextBox? _second;

    private PromptWindow(string title, string initialValue, string? secondLabel)
    {
        Title = title;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brush("Brush.Surface", Brushes.White);
        Foreground = Brush("Brush.Text", Brushes.Black);

        _input = new TextBox
        {
            Text = initialValue,
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 14,
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("Brush.Border", Brushes.Gray),
            Background = Brush("Brush.SurfaceAlt", Brushes.White),
            Foreground = Foreground,
            CaretBrush = Brush("Brush.Accent", Brushes.Black),
        };
        _input.SelectAll();
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { DialogResult = true; e.Handled = true; }
            else if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        };

        var ok = new Button { Content = "OK", Width = 74, Height = 28, IsDefault = true };
        ok.Click += (_, _) => DialogResult = true;

        var cancel = new Button { Content = "Cancel", Width = 74, Height = 28, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(_input);

        if (secondLabel is not null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = secondLabel,
                Foreground = Brush("Brush.Muted", Brushes.Gray),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
            });

            _second = new TextBox
            {
                Margin = new Thickness(0, 0, 0, 12),
                Padding = _input.Padding,
                FontSize = 14,
                BorderThickness = _input.BorderThickness,
                BorderBrush = _input.BorderBrush,
                Background = _input.Background,
                Foreground = Foreground,
                CaretBrush = _input.CaretBrush,
            };
            _second.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { DialogResult = true; e.Handled = true; }
            };
            panel.Children.Add(_second);
        }

        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }

    /// <summary>Shows the prompt and returns the entered text, or null if the user cancelled.</summary>
    public static string? Ask(Window owner, string title, string initialValue = "")
    {
        var prompt = new PromptWindow(title, initialValue, null) { Owner = owner };
        WindowBlur.Set(owner, true);
        try { return prompt.ShowDialog() == true ? prompt._input.Text : null; }
        finally { WindowBlur.Set(owner, false); }
    }

    /// <summary>Two-field variant, e.g. find and replace. Returns null if the user cancelled.</summary>
    public static (string First, string Second)? AskPair(
        Window owner, string title, string secondLabel, string initialValue = "")
    {
        var prompt = new PromptWindow(title, initialValue, secondLabel) { Owner = owner };
        WindowBlur.Set(owner, true);
        try
        {
            return prompt.ShowDialog() == true
                ? (prompt._input.Text, prompt._second?.Text ?? string.Empty)
                : null;
        }
        finally { WindowBlur.Set(owner, false); }
    }

    private static Brush Brush(string key, Brush fallback) =>
        Application.Current.TryFindResource(key) as Brush ?? fallback;
}

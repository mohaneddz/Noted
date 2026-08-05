using System.Windows;
using System.Windows.Media;

namespace Noted.Rendering;

public enum AppTheme { Dark, Light }

/// <summary>Colour + typography tokens for the editor surface. Instances are immutable and frozen.</summary>
public sealed class EditorTheme
{
    public required AppTheme Mode { get; init; }

    public required Brush Background { get; init; }
    public required Brush Surface { get; init; }
    public required Brush SurfaceAlt { get; init; }
    public required Brush Border { get; init; }
    public required Brush Text { get; init; }
    public required Brush Muted { get; init; }
    public required Brush Faint { get; init; }
    public required Brush Accent { get; init; }
    public required Brush Heading { get; init; }
    public required Brush Link { get; init; }
    public required Brush Code { get; init; }
    public required Brush CodeBackground { get; init; }
    public required Brush Quote { get; init; }
    public required Brush QuoteBar { get; init; }
    public required Brush HighlightBackground { get; init; }
    public required Brush HighlightText { get; init; }
    public required Brush RuleLine { get; init; }
    public required Brush Selection { get; init; }
    public required Brush CurrentLine { get; init; }

    /// <summary>Relative size of h1..h6 against the base editor font size.</summary>
    public static readonly double[] HeadingScale = [1.90, 1.58, 1.34, 1.18, 1.07, 1.00];

    public static EditorTheme Dark { get; } = Freeze(new EditorTheme
    {
        Mode = AppTheme.Dark,
        Background = Rgb("#17171A"),
        Surface = Rgb("#1E1E23"),
        SurfaceAlt = Rgb("#25252B"),
        Border = Rgb("#2E2E36"),
        Text = Rgb("#E7E7EC"),
        Muted = Rgb("#8A8A97"),
        Faint = Rgb("#5A5A66"),
        Accent = Rgb("#8B7CF6"),
        Heading = Rgb("#F4F4F8"),
        Link = Rgb("#6EA8FE"),
        Code = Rgb("#E8A2B4"),
        CodeBackground = Rgb("#232329"),
        Quote = Rgb("#A9A9B6"),
        QuoteBar = Rgb("#4B4B58"),
        HighlightBackground = Rgb("#54480C"),
        HighlightText = Rgb("#F7E9AE"),
        RuleLine = Rgb("#3A3A44"),
        Selection = Argb("#4C8B7CF6"),
        CurrentLine = Argb("#14FFFFFF"),
    });

    public static EditorTheme Light { get; } = Freeze(new EditorTheme
    {
        Mode = AppTheme.Light,
        Background = Rgb("#FCFCFD"),
        Surface = Rgb("#FFFFFF"),
        SurfaceAlt = Rgb("#F3F3F6"),
        Border = Rgb("#E3E3E9"),
        Text = Rgb("#22222A"),
        Muted = Rgb("#8C8C99"),
        Faint = Rgb("#B4B4C0"),
        Accent = Rgb("#6C5CE7"),
        Heading = Rgb("#101018"),
        Link = Rgb("#1B6ED0"),
        Code = Rgb("#B3305F"),
        CodeBackground = Rgb("#F2F2F6"),
        Quote = Rgb("#5C5C6B"),
        QuoteBar = Rgb("#D2D2DC"),
        HighlightBackground = Rgb("#FFF0A8"),
        HighlightText = Rgb("#4A3F00"),
        RuleLine = Rgb("#DCDCE4"),
        Selection = Argb("#406C5CE7"),
        CurrentLine = Argb("#0A000000"),
    });

    public static EditorTheme For(AppTheme mode) => mode == AppTheme.Light ? Light : Dark;

    private static SolidColorBrush Rgb(string hex) => new((Color)ColorConverter.ConvertFromString(hex)!);

    private static SolidColorBrush Argb(string hex) => new((Color)ColorConverter.ConvertFromString(hex)!);

    private static EditorTheme Freeze(EditorTheme theme)
    {
        foreach (var property in typeof(EditorTheme).GetProperties())
        {
            if (property.GetValue(theme) is Freezable { CanFreeze: true } freezable) freezable.Freeze();
        }
        return theme;
    }
}

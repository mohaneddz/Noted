using System.Windows;
using System.Windows.Media;
using Noted.Markdown;
using Noted.Services;

namespace Noted.Rendering;

public enum AppTheme { Dark, Light }

/// <summary>Colour + typography tokens for the editor surface. Instances are immutable and frozen.</summary>
public sealed class EditorTheme
{
    public required AppTheme Mode { get; init; }

    public required Brush Background { get; init; }

    /// <summary>The area outside the reading column; a shade darker than <see cref="Background"/>.</summary>
    public required Brush Margin { get; init; }

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

    /// <summary>Per-level (h1..h6) heading colour; defaults to <see cref="Heading"/> unless overridden in settings.</summary>
    public Brush[] HeadingColors { get; init; } = [];

    /// <summary>Accent colour per <see cref="CalloutKind"/> (indexed by its enum value) for callout bars and labels.</summary>
    public Brush[] CalloutColors { get; init; } = [];

    /// <summary>The bar/label colour for a callout of <paramref name="kind"/>, falling back to the quote bar.</summary>
    public Brush CalloutColor(CalloutKind kind)
    {
        int i = (int)kind;
        return i > 0 && i < CalloutColors.Length ? CalloutColors[i] : QuoteBar;
    }

    /// <summary>Draws an underline beneath heading text when true.</summary>
    public bool HeadingUnderline { get; init; }

    /// <summary>Relative size of h1..h6 against the base editor font size.</summary>
    public static readonly double[] HeadingScale = [1.90, 1.58, 1.34, 1.18, 1.07, 1.00];

    public static EditorTheme Dark { get; } = Freeze(new EditorTheme
    {
        Mode = AppTheme.Dark,
        Background = Rgb("#17171A"),
        Margin = Rgb("#0E0E11"),
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
        CalloutColors = Callouts("#6EA8FE", "#3FB950", "#A371F7", "#D29922", "#F85149"),
    });

    public static EditorTheme Light { get; } = Freeze(new EditorTheme
    {
        Mode = AppTheme.Light,
        Background = Rgb("#FCFCFD"),
        Margin = Rgb("#ECECEF"),
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
        CalloutColors = Callouts("#1B6ED0", "#1A7F37", "#8250DF", "#9A6700", "#CF222E"),
    });

    public static EditorTheme For(AppTheme mode) => mode == AppTheme.Light ? Light : Dark;

    /// <summary>Builds a theme for <paramref name="mode"/> with the user's colour and heading overrides applied.</summary>
    public static EditorTheme Resolve(AppTheme mode, AppSettings settings)
    {
        var baseTheme = For(mode);
        var colors = settings.Colors;

        var background = TryRgb(colors.Background) ?? baseTheme.Background;
        var headingBase = TryRgb(colors.Heading) ?? baseTheme.Heading;
        var headingColors = new Brush[6];
        for (int i = 0; i < 6; i++)
        {
            string? hex = i < settings.HeadingColors.Count ? settings.HeadingColors[i] : null;
            headingColors[i] = TryRgb(hex) ?? headingBase;
        }

        return Freeze(new EditorTheme
        {
            Mode = mode,
            Background = background,
            Margin = Shade(((SolidColorBrush)background).Color, mode == AppTheme.Light ? 0.94 : 0.62),
            Surface = TryRgb(colors.Surface) ?? baseTheme.Surface,
            SurfaceAlt = baseTheme.SurfaceAlt,
            Border = baseTheme.Border,
            Text = TryRgb(colors.Text) ?? baseTheme.Text,
            Muted = TryRgb(colors.Muted) ?? baseTheme.Muted,
            Faint = baseTheme.Faint,
            Accent = TryRgb(colors.Accent) ?? baseTheme.Accent,
            Heading = headingBase,
            Link = TryRgb(colors.Link) ?? baseTheme.Link,
            Code = TryRgb(colors.Code) ?? baseTheme.Code,
            CodeBackground = baseTheme.CodeBackground,
            Quote = TryRgb(colors.Quote) ?? baseTheme.Quote,
            QuoteBar = baseTheme.QuoteBar,
            HighlightBackground = baseTheme.HighlightBackground,
            HighlightText = baseTheme.HighlightText,
            RuleLine = TryRgb(colors.RuleLine) ?? baseTheme.RuleLine,
            Selection = baseTheme.Selection,
            CurrentLine = baseTheme.CurrentLine,
            HeadingColors = headingColors,
            HeadingUnderline = settings.HeadingUnderline,
            CalloutColors = baseTheme.CalloutColors,
        });
    }

    /// <summary>Parses a hex colour string, returning null for a blank/invalid/absent override.</summary>
    private static SolidColorBrush? TryRgb(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            return Rgb(hex);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static SolidColorBrush Rgb(string hex) => new((Color)ColorConverter.ConvertFromString(hex)!);

    /// <summary>Builds a frozen callout palette indexed by <see cref="CalloutKind"/> (slot 0 is the unused None).</summary>
    private static Brush[] Callouts(params string[] hex)
    {
        var brushes = new Brush[hex.Length + 1];
        for (int i = 0; i < hex.Length; i++)
        {
            var brush = Rgb(hex[i]);
            brush.Freeze();
            brushes[i + 1] = brush;
        }
        return brushes;
    }

    /// <summary>Scales a colour's channels toward black by <paramref name="factor"/> (&lt;1 darkens).</summary>
    private static SolidColorBrush Shade(Color c, double factor)
    {
        byte S(byte v) => (byte)Math.Clamp(v * factor, 0, 255);
        return new SolidColorBrush(Color.FromRgb(S(c.R), S(c.G), S(c.B)));
    }

    private static SolidColorBrush Argb(string hex) => new((Color)ColorConverter.ConvertFromString(hex)!);

    private static EditorTheme Freeze(EditorTheme theme)
    {
        foreach (var property in typeof(EditorTheme).GetProperties(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (property.GetValue(theme) is Freezable { CanFreeze: true } freezable) freezable.Freeze();
        }
        return theme;
    }
}

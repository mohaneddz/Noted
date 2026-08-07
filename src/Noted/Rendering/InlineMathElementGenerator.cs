using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using Noted.Markdown;
using WpfMath.Controls;

namespace Noted.Rendering;

/// <summary>
/// Renders inline <c>$…$</c> math as a real formula using WpfMath, on lines the caret isn't on.
/// Anything that doesn't parse as LaTeX is left as literal text, so ordinary prose containing a
/// lone dollar sign is unaffected. The raw source returns when the caret lands on the line.
/// </summary>
public sealed class InlineMathElementGenerator : VisualLineElementGenerator
{
    // $…$ — not $$ (block math), not an escaped \$, no surrounding spaces, no nested $.
    private static readonly Regex Pattern =
        new(@"(?<![\\$])\$(?!\s)(?!\$)([^$\n]+?)(?<!\s)\$(?!\$)", RegexOptions.Compiled);

    private readonly MarkdownAnalyzer _analyzer;
    private readonly RevealTracker _reveal;

    public InlineMathElementGenerator(MarkdownAnalyzer analyzer, RevealTracker reveal)
    {
        _analyzer = analyzer;
        _reveal = reveal;
    }

    public bool HideMarkers { get; set; } = true;

    public EditorTheme Theme { get; set; } = EditorTheme.Dark;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!HideMarkers) return -1;

        var line = CurrentContext.VisualLine.FirstDocumentLine;
        if (_reveal.IsRevealed(line.LineNumber) || _analyzer.IsInsideCodeBlock(line.LineNumber)) return -1;

        string text = CurrentContext.Document.GetText(line.Offset, line.Length);
        int relStart = startOffset - line.Offset;

        foreach (Match match in Pattern.Matches(text))
        {
            if (match.Index < relStart) continue;
            if (MathVisual.CanRender(match.Groups[1].Value)) return line.Offset + match.Index;
        }

        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var line = CurrentContext.VisualLine.FirstDocumentLine;
        string text = CurrentContext.Document.GetText(line.Offset, line.Length);

        foreach (Match match in Pattern.Matches(text))
        {
            if (line.Offset + match.Index != offset) continue;

            double size = CurrentContext.GlobalTextRunProperties.FontRenderingEmSize;
            var control = MathVisual.TryBuild(match.Groups[1].Value, size, Theme.Text);
            if (control is null) return null;

            control.Margin = new Thickness(1, 0, 1, 0);
            return new InlineObjectElement(match.Length, control);
        }

        return null;
    }
}

/// <summary>Shared WpfMath helpers: validate a LaTeX string and build a rendered <see cref="FormulaControl"/>.</summary>
public static class MathVisual
{
    // Parsing is the expensive part and GetFirstInterestedOffset runs on every layout pass, so cache
    // whether each unique formula renders. Bounded so a document full of distinct formulas can't grow it forever.
    private static readonly Dictionary<string, bool> Validated = new(StringComparer.Ordinal);

    // WpfMath 2.1.0 (the newest release) only implements the `pmatrix` environment, so the common
    // amsmath environments are rewritten onto it. They then render — parenthesised — instead of
    // failing to parse and falling back to raw source.
    private static readonly Regex ArrayEnvironment =
        new(@"\\begin\s*\{array\}\s*\{[^}]*\}", RegexOptions.Compiled);
    private static readonly Regex MatrixLikeEnvironment = new(
        @"\\(begin|end)\s*\{(?:matrix|bmatrix|Bmatrix|vmatrix|Vmatrix|smallmatrix|array|aligned|align|alignat|gathered|gather|cases|split)\*?\}",
        RegexOptions.Compiled);

    /// <summary>Rewrites amsmath environments WpfMath can't parse into the one it can (<c>pmatrix</c>).</summary>
    public static string Normalize(string latex)
    {
        if (latex.IndexOf(@"\begin", StringComparison.Ordinal) < 0) return latex;
        latex = ArrayEnvironment.Replace(latex, @"\begin{pmatrix}");
        return MatrixLikeEnvironment.Replace(latex, @"\$1{pmatrix}");
    }

    public static bool CanRender(string latex)
    {
        latex = Normalize(latex.Trim());
        if (latex.Length == 0) return false;
        if (Validated.TryGetValue(latex, out bool ok)) return ok;

        ok = Build(latex, 16, Brushes.Black) is not null;
        if (Validated.Count > 512) Validated.Clear();
        Validated[latex] = ok;
        return ok;
    }

    public static FormulaControl? TryBuild(string latex, double scale, Brush foreground)
        => CanRender(latex) ? Build(Normalize(latex.Trim()), scale, foreground) : null;

    private static FormulaControl? Build(string latex, double scale, Brush foreground)
    {
        try
        {
            var control = new FormulaControl
            {
                Formula = latex,
                Scale = Math.Clamp(scale, 10, 48),
                Foreground = foreground,
            };
            return control.HasError ? null : control;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

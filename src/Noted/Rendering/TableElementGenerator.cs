using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Noted.Markdown;

namespace Noted.Rendering;

/// <summary>
/// Collapses a GFM table into a single aligned grid, honouring each column's <c>:---</c> / <c>:--:</c> /
/// <c>---:</c> alignment and rendering the usual inline markup inside cells. The body lines are folded by
/// <see cref="BlockCollapser"/> so the grid can span them; moving the caret into the table brings the raw
/// pipes back for editing.
/// </summary>
public sealed class TableElementGenerator : VisualLineElementGenerator, ICollapsibleBlockSource
{
    private readonly MarkdownAnalyzer _analyzer;
    private readonly RevealTracker _reveal;

    public TableElementGenerator(MarkdownAnalyzer analyzer, RevealTracker reveal)
    {
        _analyzer = analyzer;
        _reveal = reveal;
    }

    public bool HideMarkers { get; set; } = true;

    public EditorTheme Theme { get; set; } = EditorTheme.Dark;

    public FontFamily MonospaceFont { get; set; } = new("Cascadia Mono, Consolas, Courier New");

    public double ContentWidth { get; set; }

    public IEnumerable<(int Start, int End)> CollapsedBlockRanges(TextDocument document)
    {
        if (!HideMarkers) yield break;

        foreach (var (start, end) in _analyzer.TableBlocks())
        {
            if (end <= start || _reveal.IsRangeRevealed(start, end)) continue;
            yield return (start, end);
        }
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!HideMarkers) return -1;

        var line = CurrentContext.VisualLine.FirstDocumentLine;
        if (!_analyzer.TryGetTableBlock(line.LineNumber, out int header, out int end, out _)) return -1;
        if (line.LineNumber != header || end <= header || _reveal.IsRangeRevealed(header, end)) return -1;
        if (startOffset > line.Offset) return -1;

        return line.Offset;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var document = CurrentContext.Document;
        var line = CurrentContext.VisualLine.FirstDocumentLine;
        if (!_analyzer.TryGetTableBlock(line.LineNumber, out int header, out int end, out var aligns)) return null;
        if (line.Offset != offset || line.LineNumber != header || end <= header) return null;

        var headerCells = MarkdownScanner.SplitTableCells(TextOf(document, header));
        var bodyRows = new List<List<string>>();
        for (int n = header + 2; n <= end; n++)   // header+1 is the delimiter row
            bodyRows.Add(MarkdownScanner.SplitTableCells(TextOf(document, n)));

        int columns = aligns.Length;
        columns = Math.Max(columns, headerCells.Count);
        foreach (var row in bodyRows) columns = Math.Max(columns, row.Count);
        if (columns == 0) return null;

        var container = BuildTable(headerCells, bodyRows, aligns, columns);

        var headerLine = document.GetLineByNumber(header);
        var endLine = document.GetLineByNumber(end);
        return new InlineObjectElement(endLine.EndOffset - headerLine.Offset, container);
    }

    private static string TextOf(TextDocument document, int lineNumber)
    {
        var line = document.GetLineByNumber(lineNumber);
        return document.GetText(line.Offset, line.Length);
    }

    private FrameworkElement BuildTable(
        List<string> headerCells, List<List<string>> bodyRows, ColumnAlign[] aligns, int columns)
    {
        var grid = new Grid { SnapsToDevicePixels = true };
        for (int c = 0; c < columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r <= bodyRows.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int c = 0; c < columns; c++)
            grid.Children.Add(Cell(c < headerCells.Count ? headerCells[c] : "", Align(aligns, c), 0, c, columns, header: true, zebra: false));

        for (int r = 0; r < bodyRows.Count; r++)
        {
            var row = bodyRows[r];
            bool zebra = r % 2 == 1;
            for (int c = 0; c < columns; c++)
                grid.Children.Add(Cell(c < row.Count ? row[c] : "", Align(aligns, c), r + 1, c, columns, header: false, zebra));
        }

        var outer = new Border
        {
            BorderBrush = Theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = grid,
        };

        // A table only fills the reading column up to its natural content size — capped, not forced,
        // so a narrow table doesn't stretch and a wide one scrolls horizontally instead of getting
        // cropped at the pane edge.
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 6),
            Content = outer,
        };
        if (ContentWidth > 0) scroller.MaxWidth = ContentWidth;
        return scroller;
    }

    private static ColumnAlign Align(ColumnAlign[] aligns, int column) =>
        column < aligns.Length ? aligns[column] : ColumnAlign.None;

    private Border Cell(string raw, ColumnAlign align, int row, int column, int columns, bool header, bool zebra)
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = header ? Theme.Heading : Theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = align switch
            {
                ColumnAlign.Right => TextAlignment.Right,
                ColumnAlign.Center => TextAlignment.Center,
                _ => TextAlignment.Left,
            },
        };
        if (header) text.FontWeight = FontWeights.Bold;
        foreach (var inline in BuildInlines(raw.Trim())) text.Inlines.Add(inline);

        var cell = new Border
        {
            // Interior grid lines: a hairline on the right (except the last column), a heavier rule
            // under the header, and a hairline under every other body row.
            BorderBrush = Theme.Border,
            BorderThickness = new Thickness(0, 0, column < columns - 1 ? 1 : 0, header ? 2 : 1),
            Background = header ? Theme.SurfaceAlt : zebra ? Theme.Surface : Brushes.Transparent,
            Padding = new Thickness(12, 7, 12, 7),
            Child = text,
        };
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        return cell;
    }

    /// <summary>Rebuilds a cell's inline markup by reusing the scanner: syntax markers are dropped, the
    /// remaining runs carry their bold/italic/code/strike/link/highlight styling.</summary>
    private IEnumerable<Inline> BuildInlines(string cell)
    {
        if (cell.Length == 0) return [new Run(" ")];

        var md = MarkdownScanner.Scan(cell);
        int n = cell.Length;
        var style = new MdStyle[n];
        var hidden = new bool[n];

        foreach (var token in md.Tokens)
        {
            int s = Math.Max(0, token.Offset), e = Math.Min(n, token.End);
            for (int j = s; j < e; j++)
            {
                if (token.IsMarker) hidden[j] = true;
                else style[j] |= token.Style;
            }
        }

        var runs = new List<Inline>();
        int i = 0;
        while (i < n)
        {
            if (hidden[i]) { i++; continue; }
            int j = i;
            var st = style[i];
            while (j < n && !hidden[j] && style[j] == st) j++;
            runs.Add(StyledRun(cell.Substring(i, j - i), st));
            i = j;
        }

        if (runs.Count == 0) runs.Add(new Run(" "));
        return runs;
    }

    private Run StyledRun(string text, MdStyle style)
    {
        var run = new Run(text);
        if ((style & MdStyle.Bold) != 0) run.FontWeight = FontWeights.Bold;
        if ((style & MdStyle.Italic) != 0) run.FontStyle = FontStyles.Italic;

        if ((style & MdStyle.Code) != 0)
        {
            run.FontFamily = MonospaceFont;
            run.Foreground = Theme.Code;
            run.Background = Theme.CodeBackground;
        }
        else if ((style & MdStyle.Link) != 0)
        {
            run.Foreground = Theme.Link;
            run.TextDecorations = TextDecorations.Underline;
        }

        if ((style & MdStyle.Strike) != 0) run.TextDecorations = TextDecorations.Strikethrough;
        if ((style & MdStyle.Highlight) != 0)
        {
            run.Background = Theme.HighlightBackground;
            run.Foreground = Theme.HighlightText;
        }

        return run;
    }
}

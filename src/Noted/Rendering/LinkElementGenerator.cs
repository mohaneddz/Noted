using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using Noted.Markdown;
using Noted.Services;

namespace Noted.Rendering;

/// <summary>Icons remain inline; mini and full previews occupy their own visual row.</summary>
public sealed class LinkElementGenerator(MarkdownAnalyzer analyzer, RevealTracker reveal) : VisualLineElementGenerator
{
    private readonly LinkPreviewService _previews = new();
    public LinkDisplayMode Mode { get; set; }
    public bool Enabled { get; set; } = true;
    public EditorTheme Theme { get; set; } = EditorTheme.Dark;
    public Func<string?>? NotePath { get; set; }
    public Action<int>? EditAt { get; set; }
    public double MaxWidth { get; set; } = 700;
    internal Func<LinkTarget, bool, Task<LinkPreview>>? PreviewLoader { get; set; }

    private IEnumerable<(LinkSpan Span, LinkTarget Target)> Candidates()
    {
        var line = CurrentContext.VisualLine.FirstDocumentLine;
        if (!Enabled || Mode == LinkDisplayMode.Nothing || reveal.IsRevealed(line.LineNumber)) yield break;
        string text = CurrentContext.Document.GetText(line.Offset, line.Length);
        foreach (var span in LinkSpans.Read(text, analyzer.GetLine(line.LineNumber), analyzer.ResolveLinkReference))
            if (LinkTarget.Resolve(span.Destination, NotePath?.Invoke()) is { } target)
                yield return (span, target);
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        int lineOffset = CurrentContext.VisualLine.FirstDocumentLine.Offset;
        foreach (var (span, _) in Candidates())
            if (lineOffset + span.Offset >= startOffset) return lineOffset + span.Offset;
        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        int lineOffset = CurrentContext.VisualLine.FirstDocumentLine.Offset;
        foreach (var (span, target) in Candidates())
            if (lineOffset + span.Offset == offset)
                return new LinkObjectElement(span.Length, BuildLink(span, target, offset), Mode != LinkDisplayMode.Icon, offset == lineOffset);
        return null;
    }

    private FrameworkElement BuildLink(LinkSpan span, LinkTarget target, int offset)
    {
        bool iconOnly = Mode == LinkDisplayMode.Icon;
        bool preview = Mode == LinkDisplayMode.Preview;
        double width = iconOnly ? 0 : Math.Max(100, MaxWidth - 4);
        double thumbWidth = iconOnly ? 18 : Math.Min(132, width * .23);
        double labelWidth = iconOnly ? double.PositiveInfinity : Math.Max(25, width - thumbWidth - 36);
        var row = new DockPanel { LastChildFill = true };
        var glyph = new TextBlock
        {
            Text = target.Icon,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = iconOnly ? 15 : 28,
            Foreground = Theme.Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var thumbnail = new Grid
        {
            Width = thumbWidth,
            Height = iconOnly ? 18 : 76,
            Margin = new Thickness(0, 0, iconOnly ? 5 : 12, 0),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        thumbnail.Children.Add(glyph);
        if (target.Kind == LinkKind.Folder)
        {
            glyph.Text = "";
            thumbnail.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 1,5 L 1,2 L 9,2 L 12,5 L 23,5 L 23,20 L 1,20 Z"),
                Stroke = Theme.Muted, StrokeThickness = 1.5, Stretch = Stretch.Uniform,
                Width = iconOnly ? 16 : 28, Height = iconOnly ? 16 : 28,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
        }
        var image = new Image { Width = thumbnail.Width, Height = thumbnail.Height,
            Stretch = iconOnly ? Stretch.Uniform : Stretch.UniformToFill, Visibility = Visibility.Hidden };
        thumbnail.Children.Add(image);
        DockPanel.SetDock(thumbnail, Dock.Left);
        row.Children.Add(thumbnail);

        string label = string.IsNullOrWhiteSpace(span.Label) ? target.Uri.ToString() : span.Label;
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock
        {
            Text = label,
            Foreground = Theme.Link,
            FontFamily = CurrentContext.GlobalTextRunProperties.Typeface.FontFamily,
            FontSize = CurrentContext.GlobalTextRunProperties.FontRenderingEmSize,
            TextTrimming = TextTrimming.None,
            TextWrapping = iconOnly ? TextWrapping.NoWrap : TextWrapping.Wrap,
            MaxWidth = labelWidth,
        };
        labels.Children.Add(title);
        if (!iconOnly)
        {
            labels.Children.Add(new TextBlock
            {
                Text = target.Uri.IsFile ? target.Kind.ToString() : target.Uri.Host,
                Foreground = Theme.Muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        row.Children.Add(labels);

        // Freeze the header's size before attaching it. AvalonEdit recreates a visual line whenever
        // an inline object's desired size changes; template/Loaded callbacks must not alter it.
        row.Measure(new Size(iconOnly ? double.PositiveInfinity : width - 22, double.PositiveInfinity));
        row.Width = iconOnly ? row.DesiredSize.Width : width - 22;
        row.Height = row.DesiredSize.Height;
        var content = new StackPanel { Width = row.Width };
        content.Children.Add(row);
        EmbeddedLinkPreview? player = null;
        if (preview && (target.Kind is LinkKind.Website or LinkKind.Image or LinkKind.Video or LinkKind.Audio ||
            target.Uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
        {
            player = new EmbeddedLinkPreview(target, Theme, row.Width, target.Kind == LinkKind.Audio ? 160 : 320)
            { Margin = new Thickness(0, 10, 0, 0) };
            content.Children.Add(player);
        }
        double cardHeight = row.Height + (iconOnly ? 0 : 22) + (player is null ? 0 : player.Height + 10);

        var card = new Border
        {
            Child = content,
            Width = iconOnly ? row.Width : width,
            Height = cardHeight,
            ClipToBounds = true,
            Padding = new Thickness(iconOnly ? 0 : 10),
            Margin = new Thickness(0, iconOnly ? 0 : 4, 4, iconOnly ? 0 : 4),
            CornerRadius = new CornerRadius(iconOnly ? 0 : 7),
            Background = iconOnly ? Brushes.Transparent : Theme.Surface,
            BorderBrush = Theme.Border,
            BorderThickness = new Thickness(iconOnly ? 0 : 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = target.Uri.ToString(),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        // InlineObjectRun uses the child element's bottom as its baseline unless an explicit
        // baseline is supplied. Use the editor's text baseline so the 18px favicon/file glyph
        // sits in the middle of the surrounding link text instead of sagging below it.
        if (iconOnly)
            TextBlock.SetBaselineOffset(card, CurrentContext.GlobalTextRunProperties.FontRenderingEmSize * 0.8);
        card.MouseLeftButtonDown += (_, e) => e.Handled = true;
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (!preview) Open(target);
            e.Handled = true;
        };
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open link" };
        open.Click += (_, _) => Open(target);
        var edit = new MenuItem { Header = "Edit link" };
        edit.Click += (_, _) => EditAt?.Invoke(offset);
        menu.Items.Add(open); menu.Items.Add(edit); card.ContextMenu = menu;

        bool started = false;
        card.Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            var data = await (PreviewLoader?.Invoke(target, iconOnly) ?? _previews.GetAsync(target, iconOnly));
            if (data.Image is not null)
            {
                image.Source = data.Image;
                image.Visibility = Visibility.Visible;
                glyph.Visibility = Visibility.Hidden;
            }
        };
        return card;
    }

    private static void Open(LinkTarget target)
    {
        try { Process.Start(new ProcessStartInfo(target.Uri.IsFile ? target.Uri.LocalPath : target.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        { MessageBox.Show("This link could not be opened. Check the address or file location.", "Noted"); }
    }
}

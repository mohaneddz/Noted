using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Noted.Rendering;

/// <summary>
/// Renders <c>![alt](path)</c> image links inline as the actual picture, with a corner grip to
/// resize it. A trailing <c>=WIDTHx</c> in the parentheses pins the width; dragging the grip writes
/// that token back into the document. Images render whether or not their line is "revealed" so the
/// grip stays put while you drag it — turn off live markdown (Ctrl+Shift+P) to edit the raw path.
/// </summary>
public sealed class ImageElementGenerator : VisualLineElementGenerator
{
    // ![alt](inside) — inside is the path plus an optional " =WIDTHx" size hint.
    private static readonly Regex ImagePattern = new(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex SizePattern = new(@"\s+=(\d+)(?:x\d*)?\s*$", RegexOptions.Compiled);

    private static readonly Dictionary<string, (DateTime Stamp, BitmapImage Image)> Cache = new();

    public bool HideMarkers { get; set; } = true;

    /// <summary>Upper bound on rendered width, matched to the reading column.</summary>
    public double MaxWidth { get; set; } = 800;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!HideMarkers) return -1;

        var line = CurrentContext.VisualLine.FirstDocumentLine;
        string text = CurrentContext.Document.GetText(line.Offset, line.Length);
        int relStart = startOffset - line.Offset;

        foreach (Match match in ImagePattern.Matches(text))
        {
            if (match.Index < relStart) continue;
            if (Resolve(match.Groups[2].Value, out _, out _, out _)) return line.Offset + match.Index;
        }

        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var line = CurrentContext.VisualLine.FirstDocumentLine;
        var document = CurrentContext.Document;
        string text = document.GetText(line.Offset, line.Length);

        foreach (Match match in ImagePattern.Matches(text))
        {
            if (line.Offset + match.Index != offset) continue;
            if (!Resolve(match.Groups[2].Value, out var image, out string path, out double? width)) return null;

            string alt = match.Groups[1].Value;
            var control = BuildControl(image, width, newWidth =>
            {
                string replacement = $"![{alt}]({path} ={(int)Math.Round(newWidth)}x)";
                document.Replace(line.Offset + match.Index, match.Length, replacement);
            });

            return new InlineObjectElement(match.Length, control);
        }

        return null;
    }

    private FrameworkElement BuildControl(BitmapImage source, double? width, Action<double> onResized)
    {
        double aspect = source.PixelHeight / (double)source.PixelWidth;
        double w = Math.Clamp(width ?? source.PixelWidth, 24, MaxWidth);

        var image = new Image
        {
            Source = source,
            Width = w,
            Height = w * aspect,
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true,
        };

        var grip = new Thumb
        {
            Width = 14,
            Height = 14,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE,
            Opacity = 0.6,
            Background = Brushes.White,
        };

        // Keep the click that starts a drag away from the editor, so the caret doesn't jump lines
        // (which would re-collapse the image) mid-resize.
        grip.PreviewMouseLeftButtonDown += (_, e) => e.Handled = true;
        grip.DragDelta += (_, e) =>
        {
            double next = Math.Clamp(image.Width + e.HorizontalChange, 24, MaxWidth);
            image.Width = next;
            image.Height = next * aspect;
        };
        grip.DragCompleted += (_, _) => onResized(image.Width);

        var container = new Grid { Margin = new Thickness(0, 2, 0, 2), HorizontalAlignment = HorizontalAlignment.Left };
        container.Children.Add(image);
        container.Children.Add(grip);
        return container;
    }

    /// <summary>Parses the inside of the parentheses into a loadable image plus optional pinned width.</summary>
    private static bool Resolve(string inside, out BitmapImage image, out string path, out double? width)
    {
        image = null!;
        width = null;
        path = inside.Trim();

        var size = SizePattern.Match(path);
        if (size.Success)
        {
            width = double.Parse(size.Groups[1].Value);
            path = path[..size.Index].Trim();
        }

        if (path.Length == 0 || !File.Exists(path)) return false;

        try
        {
            image = Load(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static BitmapImage Load(string path)
    {
        var stamp = File.GetLastWriteTimeUtc(path);
        if (Cache.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Image;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();

        Cache[path] = (stamp, bitmap);
        return bitmap;
    }
}

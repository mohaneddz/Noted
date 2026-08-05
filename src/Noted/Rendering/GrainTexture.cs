using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Noted.Rendering;

/// <summary>A small tiled noise texture used as a subtle film-grain overlay on the editor surface.</summary>
public static class GrainTexture
{
    private const int Size = 128;

    public static Brush Brush { get; } = Build();

    private static Brush Build()
    {
        var bitmap = new WriteableBitmap(Size, Size, 96, 96, PixelFormats.Gray8, null);
        var pixels = new byte[Size * Size];
        var random = new Random(1337);
        random.NextBytes(pixels);
        bitmap.WritePixels(new Int32Rect(0, 0, Size, Size), pixels, Size, 0);
        bitmap.Freeze();

        var brush = new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, Size, Size),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }
}

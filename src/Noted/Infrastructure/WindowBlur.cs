using System.Windows;
using System.Windows.Media.Effects;

namespace Noted.Infrastructure;

/// <summary>Blurs an owner window's whole surface while a dialog sits on top of it, so the dialog
/// reads as the focused layer instead of competing with the editor content behind it.</summary>
public static class WindowBlur
{
    public static void Set(Window? owner, bool blurred)
    {
        if (owner is null) return;
        owner.Effect = blurred ? new BlurEffect { Radius = 14 } : null;
    }
}

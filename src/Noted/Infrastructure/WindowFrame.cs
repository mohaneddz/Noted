using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace Noted.Infrastructure;

/// <summary>
/// Answers WM_NCHITTEST for the borderless main window so the custom title bar behaves like a native one.
/// Empty title bar space is caption (drag, Aero Snap, double-click maximize, right-click system menu), tabs
/// and buttons stay clickable, the window edges resize, and the maximize button reports itself as
/// HTMAXBUTTON so Windows 11 shows the snap layouts flyout over it.
/// </summary>
/// <remarks>
/// WindowChrome's own hit testing decides per element via IsHitTestVisibleInChrome, which is inherited, so
/// the stretched tab strip turned the whole bar into client area and nothing could be dragged. Hit testing
/// against the real controls under the cursor avoids having to keep those flags in sync with the layout.
/// </remarks>
public sealed class WindowFrame
{
    private const double ResizeBorder = 6;
    private const double ResizeCorner = 12;

    private readonly Window _window;
    private readonly FrameworkElement _titleBar;
    private readonly Button _maximizeButton;
    private readonly Func<bool> _isFullScreen;
    private bool _maximizeHot;

    public WindowFrame(Window window, FrameworkElement titleBar, Button maximizeButton, Func<bool> isFullScreen)
    {
        _window = window;
        _titleBar = titleBar;
        _maximizeButton = maximizeButton;
        _isFullScreen = isFullScreen;
    }

    /// <summary>Call from OnSourceInitialized, after the base call, so this hook runs ahead of WindowChrome's.</summary>
    public void Attach()
    {
        var handle = new WindowInteropHelper(_window).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                handled = true;
                return (IntPtr)HitTest(lParam);

            case WM_NCLBUTTONDOWN or WM_NCLBUTTONDBLCLK when wParam == HTMAXBUTTON:
                // Swallow it so DefWindowProc doesn't run its own caption-button tracking loop.
                handled = true;
                return IntPtr.Zero;

            case WM_NCLBUTTONUP when wParam == HTMAXBUTTON:
                handled = true;
                _window.WindowState = _window.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                return IntPtr.Zero;

            case WM_NCMOUSEMOVE:
                SetMaximizeHot(hwnd, wParam == HTMAXBUTTON);
                break;

            case WM_NCMOUSELEAVE or WM_MOUSEMOVE:
                SetMaximizeHot(hwnd, false);
                break;
        }

        return IntPtr.Zero;
    }

    private int HitTest(IntPtr lParam)
    {
        var screen = new Point((short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF));
        var point = _window.PointFromScreen(screen);

        if (_window.WindowState == WindowState.Normal && !_isFullScreen() && _window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
        {
            int edge = ResizeEdge(point, _window.ActualWidth, _window.ActualHeight);
            if (edge != HTNOWHERE) return edge;
        }

        if (!_titleBar.IsVisible) return HTCLIENT;

        var local = _window.TranslatePoint(point, _titleBar);
        if (local.X < 0 || local.Y < 0 || local.X >= _titleBar.ActualWidth || local.Y >= _titleBar.ActualHeight)
            return HTCLIENT;

        var hit = _titleBar.InputHitTest(local) as DependencyObject;
        if (IsInside(hit, _maximizeButton)) return HTMAXBUTTON;
        return IsInteractive(hit) ? HTCLIENT : HTCAPTION;
    }

    private static int ResizeEdge(Point p, double width, double height)
    {
        bool left = p.X < ResizeBorder, right = p.X >= width - ResizeBorder;
        bool top = p.Y < ResizeBorder, bottom = p.Y >= height - ResizeBorder;
        bool nearLeft = p.X < ResizeCorner, nearRight = p.X >= width - ResizeCorner;
        bool nearTop = p.Y < ResizeCorner, nearBottom = p.Y >= height - ResizeCorner;

        if ((top && nearLeft) || (left && nearTop)) return HTTOPLEFT;
        if ((top && nearRight) || (right && nearTop)) return HTTOPRIGHT;
        if ((bottom && nearLeft) || (left && nearBottom)) return HTBOTTOMLEFT;
        if ((bottom && nearRight) || (right && nearBottom)) return HTBOTTOMRIGHT;
        if (top) return HTTOP;
        if (bottom) return HTBOTTOM;
        if (left) return HTLEFT;
        if (right) return HTRIGHT;
        return HTNOWHERE;
    }

    private bool IsInteractive(DependencyObject? element)
    {
        for (; element is not null && !ReferenceEquals(element, _titleBar); element = Parent(element))
        {
            if (element is ButtonBase or ListBoxItem or ScrollBar or Thumb) return true;
        }
        return false;
    }

    private bool IsInside(DependencyObject? element, DependencyObject ancestor)
    {
        for (; element is not null && !ReferenceEquals(element, _titleBar); element = Parent(element))
        {
            if (ReferenceEquals(element, ancestor)) return true;
        }
        return false;
    }

    private static DependencyObject? Parent(DependencyObject element) =>
        element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

    /// <summary>
    /// While the cursor is over the maximize button Windows treats it as non-client, so WPF never sets
    /// IsMouseOver; mirror the caption button hover look by hand instead.
    /// </summary>
    private void SetMaximizeHot(IntPtr hwnd, bool hot)
    {
        if (hot == _maximizeHot) return;
        _maximizeHot = hot;

        if (hot)
        {
            _maximizeButton.SetResourceReference(Control.BackgroundProperty, "Brush.SurfaceAlt");
            _maximizeButton.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
            var track = new TRACKMOUSEEVENT
            {
                cbSize = Marshal.SizeOf<TRACKMOUSEEVENT>(),
                dwFlags = TME_LEAVE | TME_NONCLIENT,
                hwndTrack = hwnd,
            };
            TrackMouseEvent(ref track);
        }
        else
        {
            _maximizeButton.ClearValue(Control.BackgroundProperty);
            _maximizeButton.ClearValue(Control.ForegroundProperty);
        }
    }

    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCMOUSEMOVE = 0x00A0;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_NCLBUTTONUP = 0x00A2;
    private const int WM_NCLBUTTONDBLCLK = 0x00A3;
    private const int WM_NCMOUSELEAVE = 0x02A2;

    private const int HTNOWHERE = 0;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTMAXBUTTON = 9;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    private const uint TME_LEAVE = 0x00000002;
    private const uint TME_NONCLIENT = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT
    {
        public int cbSize;
        public uint dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
    }

    [DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT eventTrack);
}

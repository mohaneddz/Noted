using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Noted.Services;

namespace Noted.Rendering;

/// <summary>A fixed viewport: loading, navigation and disposal cannot change its measured size.
/// Standard WebView2 avoids the Windows Graphics Capture SDK dependency of the composition control.</summary>
public sealed class EmbeddedLinkPreview : Grid
{
    private readonly LinkTarget _target;
    private readonly Button _load;
    private readonly TextBlock _status;
    private WebView2? _browser;
    private int _generation;
    public Task? LoadingTask { get; private set; }
    public WebView2? Browser => _browser;
    internal string UserDataDirectory { get; set; } = Path.Combine(AppSettings.DirectoryPath, "WebView2");
    internal Exception? LastError { get; private set; }
    internal Task<bool>? NavigationTask { get; private set; }
    internal CoreWebView2WebErrorStatus? NavigationError { get; private set; }

    public EmbeddedLinkPreview(LinkTarget target, EditorTheme theme, double width, double height)
    {
        _target = target;
        Width = width; Height = height; ClipToBounds = true;
        Background = theme.Background;
        var placeholder = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _load = new Button { Content = target.Kind is LinkKind.Audio or LinkKind.Video ? "Load player" : "Load preview",
            Padding = new Thickness(18, 8, 18, 8), Background = theme.SurfaceAlt, Foreground = theme.Text, BorderBrush = theme.Border };
        _status = new TextBlock { Foreground = theme.Muted, TextWrapping = TextWrapping.Wrap, MaxWidth = Math.Max(80, width - 24),
            TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        placeholder.Children.Add(_load); placeholder.Children.Add(_status); Children.Add(placeholder);
        _load.Click += (_, e) => { LoadingTask = LoadAsync(); e.Handled = true; };
        Unloaded += (_, _) => Stop();
    }

    public async Task LoadAsync()
    {
        if (_browser is not null) return;
        int generation = ++_generation;
        LastError = null;
        _load.IsEnabled = false; _status.Text = "Loading…";
        try
        {
            var browser = new WebView2 { Width = Width, Height = Height };
            _browser = browser;
            Children.Add(browser);
            var environment = await CoreWebView2Environment.CreateAsync(null, UserDataDirectory);
            if (generation != _generation) return;
            await browser.EnsureCoreWebView2Async(environment);
            if (generation != _generation) return;
            var core = browser.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            var navigation = new TaskCompletionSource<bool>();
            NavigationTask = navigation.Task;
            core.NavigationStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var next) ||
                    (next.Scheme is not ("http" or "https") && next != _target.Uri)) e.Cancel = true;
            };
            core.NavigationCompleted += (_, e) =>
            {
                navigation.TrySetResult(e.IsSuccess);
                NavigationError = e.WebErrorStatus;
                if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled && generation == _generation)
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (generation == _generation) Fail("Preview unavailable. Use Open link to view it in another app.");
                    }));
            };
            core.ProcessFailed += (_, _) =>
            {
                if (generation == _generation) Dispatcher.BeginInvoke(() => Fail("The preview stopped. Try loading it again."));
            };
            browser.Source = _target.Uri;
            _status.Text = "";
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or COMException or
            IOException or UnauthorizedAccessException or ArgumentException or TypeLoadException or DllNotFoundException)
        {
            LastError = ex;
            if (generation == _generation) Fail("Preview unavailable. Check that Microsoft Edge WebView2 Runtime is installed, or use Open link.");
        }
    }

    private void Fail(string message) { Stop(); _status.Text = message; }

    public void Stop()
    {
        ++_generation;
        var browser = _browser;
        _browser = null;
        if (browser is not null)
        {
            // Remove the native child now, but release COM resources after the current WPF layout pass.
            Children.Remove(browser);
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => browser.Dispose()));
        }
        _load.IsEnabled = true;
        _status.Text = "";
    }
}

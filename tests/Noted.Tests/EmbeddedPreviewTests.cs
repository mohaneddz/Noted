using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using Noted.Markdown;
using Noted.Rendering;
using Noted.Services;

namespace Noted.Tests;

public class EmbeddedPreviewTests
{
    private static HwndSource Host(Visual visual, int width = 800, int height = 900) => new(new HwndSourceParameters("Noted preview test")
    {
        Width = width, Height = height, WindowStyle = unchecked((int)0x80000000),
    }) { RootVisual = visual };

    private static void PumpUntil(Func<bool> done, int timeout = 10000)
    {
        var frame = new DispatcherFrame();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (done() || watch.ElapsedMilliseconds > timeout) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        Assert.True(done(), "Timed out waiting for WPF/browser work.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadedPreviewBlocksStayLeftAlignedAndStableAfterImagesLoad(bool wrap) => MultiCaretTests.Sta(() =>
    {
        const string text = "\n**Link:** [Video](https://example.org/video.mp4)\nCompanion repo: https://example.org/repo trailing words\n";
        var editor = new TextEditor { Text = text, FontSize = 22, Foreground = EditorTheme.Dark.Text, Background = EditorTheme.Dark.Background };
        var view = editor.TextArea.TextView;
        view.Options.EnableHyperlinks = false;
        view.Options.EnableEmailHyperlinks = false;
        ((IScrollInfo)view).CanHorizontallyScroll = !wrap;
        var analyzer = new MarkdownAnalyzer(); analyzer.Attach(editor.Document);
        var reveal = new RevealTracker(); reveal.Attach(editor);
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8); bitmap.Freeze();
        int loads = 0;
        var links = new LinkElementGenerator(analyzer, reveal)
        {
            Mode = LinkDisplayMode.Preview, MaxWidth = 780,
            PreviewLoader = async (_, _) => { loads++; await Task.Delay(30); return new(Image: bitmap); },
        };
        view.ElementGenerators.Add(links);
        view.ElementGenerators.Add(new MarkdownElementGenerator(analyzer, reveal));
        view.LineTransformers.Add(new MarkdownColorizer(analyzer, reveal));
        using var host = Host(view);
        view.Measure(new Size(800, 900)); view.Arrange(new Rect(0, 0, 800, 900)); view.UpdateLayout();
        PumpUntil(() => loads >= 2 && Descendants(view).OfType<Image>().Count(i => i.Source is not null) == 2);
        int built = loads;
        var sizes = Descendants(view).OfType<EmbeddedLinkPreview>().Select(p => p.DesiredSize).ToArray();
        Assert.Equal(2, sizes.Length);
        // Drain a complete loaded/render/idle cycle: the old size-changing callbacks kept rebuilding forever.
        bool idle = false; view.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => idle = true));
        PumpUntil(() => idle);
        Assert.Equal(built, loads);
        foreach (var card in Descendants(view).OfType<Border>().Where(b => b.ContextMenu?.Items.Count == 2))
        {
            var origin = card.TransformToAncestor(view).Transform(new Point());
            Assert.InRange(origin.X, -.1, .1);
            Assert.True(card.ActualHeight > 320);
        }
        foreach (var element in view.VisualLines.SelectMany(l => l.Elements).OfType<LinkObjectElement>())
            Assert.True(element.TextRunProperties.TextDecorations is null || element.TextRunProperties.TextDecorations.Count == 0);
        Assert.Equal(text, editor.Text);
        var screenshot = new RenderTargetBitmap(800, 900, 96, 96, PixelFormats.Pbgra32);
        screenshot.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(screenshot));
        using var output = File.Create(Path.Combine(Path.GetTempPath(), "noted-embedded-blocks-test.png")); encoder.Save(output);
    });

    [Fact]
    public void StandardWebViewInitializesNavigatesAndDisposesInsideEditor() => MultiCaretTests.Sta(() =>
    {
        string fixture = Path.Combine(Path.GetTempPath(), "noted-embedded-preview-test.html");
        using var wave = new MemoryStream();
        using (var writer = new BinaryWriter(wave, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8.ToArray()); writer.Write(32036); writer.Write("WAVEfmt "u8.ToArray());
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000);
            writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8.ToArray()); writer.Write(32000); writer.Write(new byte[32000]);
        }
        File.WriteAllText(fixture, "<!doctype html><title>Noted embedded preview test</title><h1>Embedded preview works</h1>" +
            $"<audio id='media' controls muted loop src='data:audio/wav;base64,{Convert.ToBase64String(wave.ToArray())}'></audio>");
        var editor = new TextEditor { Text = $"\n[Preview]({new Uri(fixture).AbsoluteUri})\n" };
        var view = editor.TextArea.TextView;
        var analyzer = new MarkdownAnalyzer(); analyzer.Attach(editor.Document);
        var reveal = new RevealTracker(); reveal.Attach(editor);
        var generator = new LinkElementGenerator(analyzer, reveal)
        {
            Mode = LinkDisplayMode.Preview, MaxWidth = 780,
            PreviewLoader = (_, _) => Task.FromResult(new LinkPreview()),
        };
        view.ElementGenerators.Add(generator);
        using var host = Host(view);
        view.Measure(new Size(800, 900)); view.Arrange(new Rect(0, 0, 800, 900)); view.UpdateLayout();
        PumpUntil(() => Descendants(view).OfType<EmbeddedLinkPreview>().Any(p => p.IsLoaded));
        var player = Assert.Single(Descendants(view).OfType<EmbeddedLinkPreview>());
        var initial = player.DesiredSize;
        player.UserDataDirectory = Path.Combine(Path.GetTempPath(), "Noted-WebView-tests");
        // Keep the smoke test local while still exercising the actual runtime and native child window.
        Task? loading = null;
        view.Dispatcher.BeginInvoke(new Action(() => loading = player.LoadAsync()));
        PumpUntil(() => loading?.IsCompleted == true);
        loading!.GetAwaiter().GetResult();
        Assert.True(player.Browser?.CoreWebView2 is not null, player.LastError?.ToString() ?? "Browser was unloaded during initialization.");
        var browser = player.Browser!;
        PumpUntil(() => player.NavigationTask?.IsCompleted == true);
        Assert.True(player.NavigationTask!.Result, $"Navigation failed: {player.NavigationError}");
        var title = browser.ExecuteScriptAsync("document.title"); PumpUntil(() => title.IsCompleted);
        Assert.Contains("Noted embedded preview test", title.Result);
        var playback = browser.ExecuteScriptAsync("document.querySelector('#media').play(); true");
        PumpUntil(() => playback.IsCompleted);
        playback.GetAwaiter().GetResult();
        bool elapsed = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) => { elapsed = true; timer.Stop(); }; timer.Start(); PumpUntil(() => elapsed);
        var played = browser.ExecuteScriptAsync("document.querySelector('#media').currentTime > 0");
        PumpUntil(() => played.IsCompleted);
        Assert.Equal("true", played.GetAwaiter().GetResult());
        view.UpdateLayout(); Assert.Equal(initial, player.DesiredSize);
        generator.Mode = LinkDisplayMode.Icon;
        view.Redraw(); view.UpdateLayout();
        PumpUntil(() => player.Browser is null);
        bool disposed = false; view.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => disposed = true));
        PumpUntil(() => disposed);
        Assert.Null(player.Browser);
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}

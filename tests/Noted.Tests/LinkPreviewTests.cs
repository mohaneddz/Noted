using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Noted.Markdown;
using Noted.Rendering;
using Noted.Services;

namespace Noted.Tests;

public class LinkPreviewTests
{
    [Theory]
    [InlineData("https://example.org/picture.JPG?size=10", LinkKind.Image)]
    [InlineData("https://example.org/video.mp4#t=20", LinkKind.Video)]
    [InlineData("https://example.org/audio.ogg", LinkKind.Audio)]
    [InlineData("https://example.org/report.pdf?download=1", LinkKind.Document)]
    [InlineData("https://example.org/about", LinkKind.Website)]
    [InlineData("www.example.org", LinkKind.Website)]
    [InlineData("mailto:hello@example.org", LinkKind.Email)]
    [InlineData("file:///C:/Notes/", LinkKind.Folder)]
    [InlineData("C:\\Notes\\report.docx", LinkKind.Document)]
    public void ClassifiesDestinations(string url, LinkKind kind) => Assert.Equal(kind, LinkTarget.Resolve(url)!.Kind);

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,test")]
    [InlineData("shell:Downloads")]
    [InlineData("#section")]
    [InlineData("")]
    public void UnsupportedTargetsStayText(string url) => Assert.Null(LinkTarget.Resolve(url));

    [Fact]
    public void ResolvesPathsAgainstNoteAndStripsTitle()
    {
        var target = LinkTarget.Resolve("<../Images/a%20b.png> \"Photo\"", "C:\\Notes\\Drafts\\note.md");
        Assert.Equal("C:\\Notes\\Images\\a b.png", target!.Uri.LocalPath);
        Assert.Equal(LinkKind.Image, target.Kind);
        Assert.Null(LinkTarget.Resolve("relative.png"));
    }

    [Theory]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?t=5")]
    [InlineData("https://www.youtube.com/watch?feature=share&v=dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ")]
    public void RecognizesYouTubeVariants(string url) => Assert.Equal("dQw4w9WgXcQ", LinkTarget.Resolve(url)!.VideoId);

    [Fact]
    public void DoesNotMistakeLookalikeHostForYouTube() =>
        Assert.Null(LinkTarget.Resolve("https://youtube.com.evil.example/watch?v=dQw4w9WgXcQ")!.VideoId);

    [Theory]
    [InlineData("[Site](https://example.org/a_(b) \"Title\")", "https://example.org/a_(b) \"Title\"")]
    [InlineData("<https://example.org>", "https://example.org")]
    [InlineData("https://example.org", "https://example.org")]
    public void ExtractsWholeSpan(string source, string destination)
    {
        var span = Assert.Single(LinkSpans.Read(source, MarkdownScanner.Scan(source)));
        Assert.Equal(source.Length, span.Length);
        Assert.Equal(0, span.Offset);
        Assert.Equal(destination, span.Destination);
    }

    [Theory]
    [InlineData("`https://example.org`")]
    [InlineData("![Image](https://example.org/photo.png)")]
    [InlineData("<!-- https://example.org -->")]
    public void LeavesCodeImagesAndEscapedSyntaxAlone(string source) =>
        Assert.Empty(LinkSpans.Read(source, MarkdownScanner.Scan(source)));

    [Fact]
    public void MetadataSupportsReorderedAttributesAndEntities()
    {
        var data = LinkPreviewService.ParseMetadata("<meta content='A &amp; B' property='og:title'><meta name=description content=Summary><meta content=\"/image.png\" property=\"og:image\">");
        Assert.Equal("A & B", data["og:title"]);
        Assert.Equal("Summary", data["description"]);
        Assert.Equal("/image.png", data["og:image"]);
    }

    [Fact]
    public void ReferenceLinksFollowDefinitionEditsAndIgnoreFences()
    {
        var document = new TextDocument("[Site][id]\n[id]: https://example.org/first\n```\n[ignored]: https://example.org\n```");
        var analyzer = new MarkdownAnalyzer(); analyzer.Attach(document);
        var span = Assert.Single(LinkSpans.Read("[Site][id]", analyzer.GetLine(1), analyzer.ResolveLinkReference));
        Assert.Equal("https://example.org/first", span.Destination);
        Assert.Null(analyzer.ResolveLinkReference("ignored"));
        document.Text = "[Site][id]\n[id]: https://example.org/second";
        Assert.Equal("https://example.org/second", analyzer.ResolveLinkReference("ID"));
        document.Text = "[Site][id]";
        Assert.Null(analyzer.ResolveLinkReference("id"));
    }

    [Fact]
    public async Task MissingImageReturnsFallbackAndUsesCachedTask()
    {
        var service = new LinkPreviewService();
        var target = LinkTarget.Resolve("file:///C:/nonexistent-noted-test-folder/image.png")!;
        var task = service.GetAsync(target, false);
        Assert.Same(task, service.GetAsync(target, false));
        Assert.Null((await task).Image);
    }

    [Fact]
    public void SettingsRoundTripEveryMode()
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        Assert.Equal(LinkDisplayMode.Nothing, JsonSerializer.Deserialize<AppSettings>("{}", options)!.LinkDisplayMode);
        foreach (var mode in Enum.GetValues<LinkDisplayMode>())
        {
            string json = JsonSerializer.Serialize(new AppSettings { LinkDisplayMode = mode }, options);
            Assert.Equal(mode, JsonSerializer.Deserialize<AppSettings>(json, options)!.LinkDisplayMode);
        }
    }

    [Fact]
    public void RendersAllModesWithoutChangingDocumentAndRevealsForEditing()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                const string source = "\n[Report](file:///C:/Notes/report.pdf)\n[Folder](file:///C:/Notes/)\n[Image](file:///C:/Notes/photo.png)\n";
                var editor = new TextEditor { Text = source, Width = 700, Height = 700, FontSize = 16 };
                var view = editor.TextArea.TextView;
                var analyzer = new MarkdownAnalyzer(); analyzer.Attach(editor.Document);
                var reveal = new RevealTracker(); reveal.Attach(editor);
                var generator = new LinkElementGenerator(analyzer, reveal);
                editor.TextArea.TextView.ElementGenerators.Add(generator);
                foreach (var mode in Enum.GetValues<LinkDisplayMode>())
                {
                    generator.Mode = mode;
                    editor.TextArea.TextView.Redraw();
                    view.Measure(new Size(700, 700)); view.Arrange(new Rect(0, 0, 700, 700)); view.UpdateLayout();
                    editor.TextArea.TextView.EnsureVisualLines();
                    var cards = Descendants(view).OfType<Border>().Where(b => b.ContextMenu?.Items.Count == 2).ToArray();
                    Assert.True(cards.Length == (mode == LinkDisplayMode.Nothing ? 0 : 3), $"Mode {mode}: {cards.Length} cards; lines {editor.TextArea.TextView.VisualLines.Count}; visual children {Descendants(view).Count()}; elements {string.Join(",", editor.TextArea.TextView.VisualLines.SelectMany(l => l.Elements).Select(e => e.GetType().Name))}");
                    Assert.Equal(source, editor.Text);
                    if (mode != LinkDisplayMode.Nothing)
                    {
                        var bitmap = new RenderTargetBitmap(700, 700, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(view);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var output = File.Create(Path.Combine(Path.GetTempPath(), $"noted-link-{mode}-test.png")); encoder.Save(output);
                    }
                }
                editor.TextArea.Caret.Line = 2;
                editor.TextArea.TextView.Redraw(); view.UpdateLayout(); editor.TextArea.TextView.EnsureVisualLines();
                Assert.Equal(2, Descendants(view).OfType<Border>().Count(b => b.ContextMenu?.Items.Count == 2));
                generator.Enabled = false;
                editor.TextArea.TextView.Redraw(); view.UpdateLayout(); editor.TextArea.TextView.EnsureVisualLines();
                Assert.DoesNotContain(Descendants(view).OfType<Border>(), b => b.ContextMenu?.Items.Count == 2);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void IconModeUsesTheEditorTextBaseline()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var editor = new TextEditor { Text = "\nbefore [Site](https://example.org) after", FontSize = 24 };
                var analyzer = new MarkdownAnalyzer(); analyzer.Attach(editor.Document);
                var reveal = new RevealTracker(); reveal.Attach(editor);
                editor.CaretOffset = 0;
                var links = new LinkElementGenerator(analyzer, reveal) { Mode = LinkDisplayMode.Icon };
                var view = editor.TextArea.TextView; view.ElementGenerators.Add(links);
                view.Measure(new Size(900, 200)); view.Arrange(new Rect(0, 0, 900, 200)); view.UpdateLayout(); view.EnsureVisualLines();
                var card = Assert.Single(Descendants(view).OfType<Border>(), b => b.ContextMenu?.Items.Count == 2);
                Assert.Equal(9.6, TextBlock.GetBaselineOffset(card), 3);
                Assert.Equal(VerticalAlignment.Center, Descendants(card).OfType<Grid>().First().VerticalAlignment);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void FullPreviewHasStableDimensionsAtNarrowAndWideReadingWidths()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var editor = new TextEditor { Text = "\n[Website](https://example.org)\n", FontSize = 16 };
                var analyzer = new MarkdownAnalyzer(); analyzer.Attach(editor.Document);
                var reveal = new RevealTracker(); reveal.Attach(editor);
                var generator = new LinkElementGenerator(analyzer, reveal) { Mode = LinkDisplayMode.Preview };
                var view = editor.TextArea.TextView;
                view.ElementGenerators.Add(generator);
                foreach (var width in new[] { 180d, 700d })
                {
                    generator.MaxWidth = width;
                    view.Measure(new Size(width, 700)); view.Arrange(new Rect(0, 0, width, 700)); view.UpdateLayout();
                    view.EnsureVisualLines();
                    var card = Assert.Single(Descendants(view).OfType<Border>(), b => b.ContextMenu?.Items.Count == 2);
                    Assert.Equal(width - 4, card.ActualWidth, 1);
                    Assert.True(double.IsFinite(card.ActualHeight) && card.ActualHeight > 0);
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}

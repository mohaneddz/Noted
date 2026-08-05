using Noted.Markdown;

namespace Noted.Tests;

public class MarkdownScannerTests
{
    private static MdLine Scan(string line) => MarkdownScanner.Scan(line);

    private static string Rendered(string line)
    {
        // What the reader sees once markers are hidden: the line minus every marker span.
        var info = Scan(line);
        var keep = new bool[line.Length];
        Array.Fill(keep, true);

        foreach (var token in info.Tokens)
        {
            if (!token.IsMarker) continue;
            for (int i = token.Offset; i < token.End && i < line.Length; i++) keep[i] = false;
        }

        return string.Concat(line.Where((_, i) => keep[i]));
    }

    private static MdToken ContentAt(string line, string text)
    {
        int offset = line.IndexOf(text, StringComparison.Ordinal);
        return Scan(line).Tokens.Single(t => !t.IsMarker && t.Offset == offset && t.Length == text.Length);
    }

    // ---------------- headings ----------------

    [Theory]
    [InlineData("# One", 1)]
    [InlineData("## Two", 2)]
    [InlineData("###### Six", 6)]
    public void RecognisesHeadingLevels(string line, int expected)
    {
        var info = Scan(line);

        Assert.Equal(expected, info.HeadingLevel);
        Assert.True((info.Block & MdStyle.Heading) != 0);
    }

    [Theory]
    [InlineData("####### Seven hashes is not a heading")]
    [InlineData("#NoSpace")]
    [InlineData("a # mid-line hash")]
    public void RejectsNonHeadings(string line) => Assert.Equal(0, Scan(line).HeadingLevel);

    [Fact]
    public void HeadingHidesItsHashesAndTheFollowingSpace()
        => Assert.Equal("Title", Rendered("## Title"));

    // ---------------- emphasis ----------------

    [Fact]
    public void BoldMarkersWrapBoldContent()
    {
        var token = ContentAt("a **strong** b", "strong");

        Assert.True((token.Style & MdStyle.Bold) != 0);
        Assert.Equal("a strong b", Rendered("a **strong** b"));
    }

    [Fact]
    public void TripleAsteriskIsBoldAndItalic()
    {
        var token = ContentAt("***both***", "both");

        Assert.True((token.Style & MdStyle.Bold) != 0);
        Assert.True((token.Style & MdStyle.Italic) != 0);
    }

    [Fact]
    public void EmphasisNestsInsideEmphasis()
    {
        const string line = "**bold with *italic* inside**";

        var inner = ContentAt(line, "italic");
        Assert.True((inner.Style & MdStyle.Bold) != 0);
        Assert.True((inner.Style & MdStyle.Italic) != 0);
    }

    [Fact]
    public void UnderscoresInsideWordsAreNotEmphasis()
        => Assert.Equal("snake_case_word", Rendered("snake_case_word"));

    [Fact]
    public void UnmatchedMarkerIsLeftAlone()
        => Assert.Equal("half **open", Rendered("half **open"));

    [Fact]
    public void EmptyEmphasisStaysLiteral()
        => Assert.Equal("a **** b", Rendered("a **** b"));

    [Fact]
    public void AsteriskRunOnItsOwnLineIsARuleNotEmphasis()
        => Assert.True((Scan("****").Block & MdStyle.Rule) != 0);

    // ---------------- code, strike, highlight ----------------

    [Fact]
    public void InlineCodeIsNotParsedForEmphasis()
    {
        const string line = "use `a *b* c` here";

        var token = ContentAt(line, "a *b* c");
        Assert.True((token.Style & MdStyle.Code) != 0);
        Assert.Equal("use a *b* c here", Rendered(line));
    }

    [Fact]
    public void StrikethroughAndHighlightAreRecognised()
    {
        Assert.True((ContentAt("~~gone~~", "gone").Style & MdStyle.Strike) != 0);
        Assert.True((ContentAt("==lit==", "lit").Style & MdStyle.Highlight) != 0);
    }

    // ---------------- links ----------------

    [Fact]
    public void LinkKeepsLabelAndHidesTarget()
    {
        const string line = "see [the docs](https://example.com) now";

        var token = ContentAt(line, "the docs");
        Assert.True((token.Style & MdStyle.Link) != 0);
        Assert.Equal("see the docs now", Rendered(line));
    }

    [Fact]
    public void ImageIsMarkedAsImage()
        => Assert.True((ContentAt("![alt](pic.png)", "alt").Style & MdStyle.Image) != 0);

    [Fact]
    public void AutolinkKeepsTheUrlVisible()
        => Assert.Equal("https://example.com", Rendered("<https://example.com>"));

    [Fact]
    public void BracketsThatAreNotLinksStayPut()
        => Assert.Equal("[just brackets]", Rendered("[just brackets]"));

    // ---------------- lists and tasks ----------------

    [Theory]
    [InlineData("- item")]
    [InlineData("* item")]
    [InlineData("+ item")]
    [InlineData("  - indented item")]
    public void BulletListsAreRecognised(string line)
    {
        var info = Scan(line);

        Assert.True((info.Block & MdStyle.ListMarker) != 0);
        Assert.Contains(info.Tokens, t => (t.Style & MdStyle.Bullet) != 0);
    }

    [Theory]
    [InlineData("1. item")]
    [InlineData("42) item")]
    public void OrderedListsAreRecognisedButNotBullets(string line)
    {
        var info = Scan(line);

        Assert.True((info.Block & MdStyle.ListMarker) != 0);
        Assert.DoesNotContain(info.Tokens, t => (t.Style & MdStyle.Bullet) != 0);
    }

    [Fact]
    public void DashWithoutSpaceIsNotAList()
        => Assert.False((Scan("-notalist").Block & MdStyle.ListMarker) != 0);

    [Theory]
    [InlineData("- [ ] todo", false)]
    [InlineData("- [x] done", true)]
    [InlineData("- [X] done", true)]
    public void TaskBoxesAreRecognised(string line, bool expectedChecked)
    {
        var info = Scan(line);

        Assert.True((info.Block & MdStyle.Task) != 0);
        Assert.Equal(expectedChecked, (info.Block & MdStyle.TaskChecked) != 0);
    }

    // ---------------- quotes and rules ----------------

    [Theory]
    [InlineData("> one", 1)]
    [InlineData("> > two", 2)]
    [InlineData(">> two", 2)]
    public void QuoteDepthIsCounted(string line, int expected)
        => Assert.Equal(expected, Scan(line).QuoteDepth);

    [Theory]
    [InlineData("---")]
    [InlineData("***")]
    [InlineData("___")]
    [InlineData("- - -")]
    [InlineData("-----")]
    public void HorizontalRulesAreRecognised(string line)
        => Assert.True((Scan(line).Block & MdStyle.Rule) != 0);

    [Theory]
    [InlineData("--")]
    [InlineData("- - a")]
    public void NearMissesAreNotRules(string line)
        => Assert.False((Scan(line).Block & MdStyle.Rule) != 0);

    // ---------------- the all-markers guard ----------------

    [Fact]
    public void LineOfPureSyntaxIsFlaggedSoItIsNeverCollapsed()
    {
        Assert.True(Scan("---").AllMarkers);
        Assert.True(Scan("## ").AllMarkers);
    }

    [Fact]
    public void LineWithContentIsNotFlaggedAsAllMarkers()
    {
        Assert.False(Scan("## Title").AllMarkers);
        Assert.False(Scan("- item").AllMarkers);
    }

    // ---------------- offsets stay inside the line ----------------

    [Theory]
    [InlineData("# Heading with **bold** and `code`")]
    [InlineData("> - [x] **quoted task** with [link](url)")]
    [InlineData("***")]
    [InlineData("")]
    [InlineData("\\*escaped\\*")]
    public void EveryTokenStaysWithinTheLine(string line)
    {
        foreach (var token in Scan(line).Tokens)
        {
            Assert.True(token.Offset >= 0, $"negative offset in “{line}”");
            Assert.True(token.End <= line.Length, $"token past end of “{line}”");
            Assert.True(token.Length > 0, $"empty token in “{line}”");
        }
    }
}

using ICSharpCode.AvalonEdit.Document;
using Noted.Markdown;

namespace Noted.Tests;

public class MarkdownAnalyzerTests
{
    private static MarkdownAnalyzer AnalyzerFor(string text)
    {
        var analyzer = new MarkdownAnalyzer();
        analyzer.Attach(new TextDocument(text));
        return analyzer;
    }

    [Fact]
    public void FencedLinesAreTreatedAsCode()
    {
        var analyzer = AnalyzerFor("before\n```\n# not a heading\n```\nafter");

        Assert.False(analyzer.IsInsideCodeBlock(1));
        Assert.True(analyzer.IsInsideCodeBlock(2));   // opening fence
        Assert.True(analyzer.IsInsideCodeBlock(3));   // content
        Assert.True(analyzer.IsInsideCodeBlock(4));   // closing fence
        Assert.False(analyzer.IsInsideCodeBlock(5));
    }

    [Fact]
    public void MarkdownInsideAFenceIsNotParsed()
    {
        var analyzer = AnalyzerFor("```\n# not a heading\n```");

        Assert.Equal(0, analyzer.GetLine(2).HeadingLevel);
        Assert.True((analyzer.GetLine(2).Block & MdStyle.CodeBlock) != 0);
    }

    [Fact]
    public void TildeFencesWorkAndDoNotCloseBacktickFences()
    {
        var analyzer = AnalyzerFor("~~~\ncode\n~~~\n# heading");

        Assert.True(analyzer.IsInsideCodeBlock(2));
        Assert.False(analyzer.IsInsideCodeBlock(4));
        Assert.Equal(1, analyzer.GetLine(4).HeadingLevel);
    }

    [Fact]
    public void AnUnclosedFenceSwallowsTheRestOfTheDocument()
    {
        var analyzer = AnalyzerFor("```\none\ntwo");

        Assert.True(analyzer.IsInsideCodeBlock(2));
        Assert.True(analyzer.IsInsideCodeBlock(3));
    }

    [Fact]
    public void ClosingFenceMustBeAtLeastAsLongAsTheOpener()
    {
        var analyzer = AnalyzerFor("````\n```\nstill code\n````\nout");

        Assert.True(analyzer.IsInsideCodeBlock(2));
        Assert.True(analyzer.IsInsideCodeBlock(3));
        Assert.False(analyzer.IsInsideCodeBlock(5));
    }

    [Fact]
    public void EditingTheDocumentRefreshesTheCache()
    {
        var document = new TextDocument("plain line");
        var analyzer = new MarkdownAnalyzer();
        analyzer.Attach(document);

        Assert.Equal(0, analyzer.GetLine(1).HeadingLevel);

        document.Insert(0, "## ");

        Assert.Equal(2, analyzer.GetLine(1).HeadingLevel);
    }

    [Fact]
    public void OutOfRangeLinesReturnEmptyInsteadOfThrowing()
    {
        var analyzer = AnalyzerFor("one line");

        Assert.Empty(analyzer.GetLine(0).Tokens);
        Assert.Empty(analyzer.GetLine(99).Tokens);
    }
}

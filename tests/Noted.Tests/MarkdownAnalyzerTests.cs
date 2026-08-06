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

    // ---------------- fence block ranges + language ----------------

    [Fact]
    public void CodeBlockRangeCoversFenceAndContent()
    {
        var analyzer = AnalyzerFor("before\n```csharp\nvar x = 1;\nvar y = 2;\n```\nafter");

        for (int line = 2; line <= 5; line++)
        {
            Assert.True(analyzer.TryGetCodeBlock(line, out int start, out int end, out string language));
            Assert.Equal(2, start);
            Assert.Equal(5, end);
            Assert.Equal("csharp", language);
        }

        Assert.False(analyzer.TryGetCodeBlock(1, out _, out _, out _));
        Assert.False(analyzer.TryGetCodeBlock(6, out _, out _, out _));
    }

    [Fact]
    public void LanguageIsEmptyWhenTheFenceHasNone()
    {
        var analyzer = AnalyzerFor("```\ncode\n```");

        Assert.True(analyzer.TryGetCodeBlock(1, out _, out _, out string language));
        Assert.Equal(string.Empty, language);
    }

    [Fact]
    public void UnclosedFenceStillReportsARangeToEndOfDocument()
    {
        var analyzer = AnalyzerFor("```python\none\ntwo");

        Assert.True(analyzer.TryGetCodeBlock(3, out int start, out int end, out string language));
        Assert.Equal(1, start);
        Assert.Equal(3, end);
        Assert.Equal("python", language);
    }

    // ---------------- tables ----------------

    [Fact]
    public void DelimiterRowTurnsTheLineAboveIntoAHeaderAndBelowIntoRows()
    {
        var analyzer = AnalyzerFor("intro\n\n| A | B |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |\n\nafter");

        Assert.Equal(MdStyle.None, analyzer.GetLine(1).Block & MdStyle.Table);
        Assert.True((analyzer.GetLine(3).Block & MdStyle.TableHeader) != 0);
        Assert.True((analyzer.GetLine(4).Block & MdStyle.TableDelimiter) != 0);
        Assert.True((analyzer.GetLine(5).Block & MdStyle.Table) != 0);
        Assert.True((analyzer.GetLine(6).Block & MdStyle.Table) != 0);
        Assert.Equal(MdStyle.None, analyzer.GetLine(8).Block & MdStyle.Table);   // blank line ends the table
    }

    [Fact]
    public void ADashRuleIsNotMistakenForATableDelimiter()
    {
        var analyzer = AnalyzerFor("Some text\n---\nmore");

        Assert.Equal(MdStyle.None, analyzer.GetLine(1).Block & MdStyle.TableHeader);
        Assert.True((analyzer.GetLine(2).Block & MdStyle.Rule) != 0);
        Assert.Equal(MdStyle.None, analyzer.GetLine(2).Block & MdStyle.Table);
    }

    [Fact]
    public void TablesInsideCodeFencesAreLeftAlone()
    {
        var analyzer = AnalyzerFor("```\n| A | B |\n|---|---|\n```");

        Assert.True((analyzer.GetLine(2).Block & MdStyle.CodeBlock) != 0);
        Assert.Equal(MdStyle.None, analyzer.GetLine(2).Block & MdStyle.Table);
    }

    // ---------------- setext headings ----------------

    [Theory]
    [InlineData("=====", 1)]
    [InlineData("-----", 2)]
    public void ParagraphAboveASetextUnderlineBecomesAHeading(string underline, int level)
    {
        var analyzer = AnalyzerFor($"Alt Heading\n{underline}\n\nbody");

        Assert.Equal(level, analyzer.GetLine(1).HeadingLevel);
        Assert.True((analyzer.GetLine(1).Block & MdStyle.Heading) != 0);
        Assert.True((analyzer.GetLine(2).Block & MdStyle.Rule) != 0);   // underline drawn as a rule
        Assert.Equal(0, analyzer.GetLine(4).HeadingLevel);
    }

    [Fact]
    public void DashesAfterABlankLineStayAThematicBreakNotASetextHeading()
    {
        var analyzer = AnalyzerFor("intro\n\n---\n\nmore");

        Assert.Equal(0, analyzer.GetLine(1).HeadingLevel);   // blank line between, so no setext
        Assert.True((analyzer.GetLine(3).Block & MdStyle.Rule) != 0);
    }

    [Fact]
    public void SetextUnderlineDoesNotAttachToAHeadingOrListLine()
    {
        var analyzer = AnalyzerFor("# ATX heading\n---");

        Assert.Equal(1, analyzer.GetLine(1).HeadingLevel);   // stays an ATX h1, not reinterpreted
        Assert.True((analyzer.GetLine(2).Block & MdStyle.Rule) != 0);
    }

    [Fact]
    public void SeparateFencesAreSeparateBlocks()
    {
        var analyzer = AnalyzerFor("```a\none\n```\ntext\n```b\ntwo\n```");

        Assert.True(analyzer.TryGetCodeBlock(2, out int start1, out int end1, out string lang1));
        Assert.Equal((1, 3, "a"), (start1, end1, lang1));

        Assert.True(analyzer.TryGetCodeBlock(7, out int start2, out int end2, out string lang2));
        Assert.Equal((5, 7, "b"), (start2, end2, lang2));
    }
}

using System.Text;
using OrchDesk.Core.Presentation;
using Xunit;

namespace OrchDesk.Tests.Baseline;

public sealed class MarkdownTests
{
    private static string Text(IEnumerable<MdSpan> spans) => string.Concat(spans.Select(s => s.Text));

    private static IReadOnlyList<MdSpan> OnlySpans(IReadOnlyList<MdBlock> blocks) =>
        Assert.IsType<Paragraph>(Assert.Single(blocks)).Spans;

    [Fact]
    public void HeadingsHaveLevelsAndText()
    {
        var blocks = MarkdownParser.Parse("# One\n## Two\n###### Six\n####### not a heading");
        Assert.Equal(4, blocks.Count);
        Assert.Equal(1, Assert.IsType<Heading>(blocks[0]).Level);
        Assert.Equal("One", Text(Assert.IsType<Heading>(blocks[0]).Spans));
        Assert.Equal(2, Assert.IsType<Heading>(blocks[1]).Level);
        Assert.Equal(6, Assert.IsType<Heading>(blocks[2]).Level);
        Assert.IsType<Paragraph>(blocks[3]);
    }

    [Fact]
    public void NestedEmphasisKeepsBoldAndItalic()
    {
        var spans = OnlySpans(MarkdownParser.Parse("**bold *and italic* text**"));
        var inner = Assert.Single(spans, s => s.Text == "and italic");
        Assert.True(inner.Bold);
        Assert.True(inner.Italic);
        Assert.All(spans, s => Assert.True(s.Bold));
    }

    [Fact]
    public void UnderscoreItalicAndSnakeCaseLiteral()
    {
        var spans = OnlySpans(MarkdownParser.Parse("_it_ and snake_case_name"));
        Assert.Contains(spans, s => s.Text == "it" && s.Italic);
        Assert.Equal("it and snake_case_name", Text(spans));
        Assert.DoesNotContain(spans, s => s.Italic && s.Text.Contains("case"));
    }

    [Fact]
    public void UnclosedBoldStaysLiteral()
    {
        var spans = OnlySpans(MarkdownParser.Parse("**abc"));
        Assert.Equal("**abc", Text(spans));
        Assert.All(spans, s => Assert.False(s.Bold));
    }

    [Fact]
    public void InlineCodeIsCodeSpan()
    {
        var spans = OnlySpans(MarkdownParser.Parse("use `a*b*c` here"));
        Assert.Contains(spans, s => s.Code && s.Text == "a*b*c");
        Assert.Equal("use a*b*c here", Text(spans));
    }

    [Fact]
    public void ClosedCodeFenceWithLanguage()
    {
        var block = Assert.IsType<CodeBlock>(Assert.Single(MarkdownParser.Parse("```csharp\nvar x = 1;\n```")));
        Assert.Equal("csharp", block.Language);
        Assert.Equal("var x = 1;", block.Code);
    }

    [Fact]
    public void UnclosedCodeFenceRendersTextSoFar()
    {
        var block = Assert.IsType<CodeBlock>(Assert.Single(MarkdownParser.Parse("```csharp\nvar x = 1;\nvar y")));
        Assert.Equal("csharp", block.Language);
        Assert.Equal("var x = 1;\nvar y", block.Code);
    }

    [Fact]
    public void UnclosedFenceWithTrailingNewlineDropsTrailingBlankLine()
    {
        var block = Assert.IsType<CodeBlock>(Assert.Single(MarkdownParser.Parse("```\nabc\n")));
        Assert.Equal("abc", block.Code);
    }

    [Fact]
    public void ListsOrderedAndUnordered()
    {
        var blocks = MarkdownParser.Parse("- a\n- b\n\n1. c\n2. d");
        Assert.Equal(2, blocks.Count);
        var bullets = Assert.IsType<ListBlock>(blocks[0]);
        Assert.False(bullets.Ordered);
        Assert.Equal(2, bullets.Items.Count);
        Assert.Equal("b", Text(bullets.Items[1]));
        var numbered = Assert.IsType<ListBlock>(blocks[1]);
        Assert.True(numbered.Ordered);
        Assert.Equal(2, numbered.Items.Count);
    }

    [Fact]
    public void StarListAndRuleAreDistinct()
    {
        var blocks = MarkdownParser.Parse("* one\n* two\n\n---");
        Assert.IsType<ListBlock>(blocks[0]);
        Assert.IsType<Rule>(blocks[1]);
    }

    [Fact]
    public void QuoteCollectsLines()
    {
        var quote = Assert.IsType<Quote>(Assert.Single(MarkdownParser.Parse("> first\n> second")));
        Assert.Equal("first\nsecond", Text(quote.Spans));
    }

    [Fact]
    public void PipeTableWithHeaderAndRows()
    {
        var table = Assert.IsType<Table>(Assert.Single(MarkdownParser.Parse("| a | b |\n|---|:---:|\n| 1 | 2 |\n| 3 | 4 |")));
        Assert.Equal(2, table.Header.Count);
        Assert.Equal("a", Text(table.Header[0]));
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("4", Text(table.Rows[1][1]));
    }

    [Fact]
    public void TableWithoutDelimiterIsParagraph()
    {
        Assert.IsType<Paragraph>(Assert.Single(MarkdownParser.Parse("| a | b |")));
    }

    [Fact]
    public void LinkHasUrlAndText()
    {
        var spans = OnlySpans(MarkdownParser.Parse("see [docs](https://example.com/z) now"));
        var link = Assert.Single(spans, s => s.Link is not null);
        Assert.Equal("docs", link.Text);
        Assert.Equal("https://example.com/z", link.Link);
        Assert.Equal("see docs now", Text(spans));
    }

    [Fact]
    public void IncompleteLinkStaysLiteral()
    {
        var spans = OnlySpans(MarkdownParser.Parse("[docs](https://exa"));
        Assert.All(spans, s => Assert.Null(s.Link));
        Assert.Equal("[docs](https://exa", Text(spans));
    }

    [Fact]
    public void NullAndEmptyYieldNoBlocks()
    {
        Assert.Empty(MarkdownParser.Parse(null!));
        Assert.Empty(MarkdownParser.Parse(""));
    }

    [Fact]
    public void GarbageInputNeverThrows()
    {
        const string alphabet = "#*_`[]()>-|:~ \t\n\r\\1.)aZ!<>\"'{}&;é\U0001F600";
        var rng = new Random(20261017);
        for (int round = 0; round < 200; round++)
        {
            int length = rng.Next(0, 300);
            var sb = new StringBuilder(length);
            for (int k = 0; k < length; k++) sb.Append(alphabet[rng.Next(alphabet.Length)]);
            string input = sb.ToString();

            var ex = Record.Exception(() => MarkdownParser.Parse(input));
            Assert.Null(ex);
        }
    }
}

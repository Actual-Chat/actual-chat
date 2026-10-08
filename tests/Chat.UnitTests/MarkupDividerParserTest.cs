namespace ActualChat.Chat.UnitTests;

public class MarkupDividerParserTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly IMarkupParser Parser = new MarkupParser();

    [Theory]
    [InlineData("---")]
    [InlineData("-----------")]
    [InlineData("---   ")]
    public void DividerLineShouldParseToDivider(string text)
    {
        // act
        var markup = Parser.Parse(text);

        // assert
        markup.Should().BeOfType<DividerMarkup>();
    }

    [Fact]
    public void DividerShouldFormatAsThreeDashes()
    {
        // act
        var markup = Parser.Parse("-----");

        // assert
        markup.Format().Should().Be("---");
        MarkupFormatter.Default.Format(markup).Should().Be("---");
    }

    [Fact]
    public void DividerShouldSplitSurroundingParagraphs()
    {
        // arrange
        const string text = "above\n---\nbelow";

        // act
        var markup = Parser.Parse(text);

        // assert
        var seq = markup.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items.Length.Should().Be(3);
        seq.Items[0].Should().BeOfType<ParagraphMarkup>();
        seq.Items[1].Should().BeOfType<DividerMarkup>();
        seq.Items[2].Should().BeOfType<ParagraphMarkup>();
        markup.Format().Replace("\r\n", "\n").Should().Be(text);
    }

    [Fact]
    public void DividerShouldEndList()
    {
        // act
        var markup = Parser.Parse("- one\n- two\n---\nafter");

        // assert
        var seq = markup.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items[0].Should().BeOfType<ListMarkup>().Which.Items.Length.Should().Be(2);
        seq.Items[1].Should().BeOfType<DividerMarkup>();
        seq.Items[2].Should().BeOfType<ParagraphMarkup>();
    }

    [Fact]
    public void DividerShouldWorkInsideBlockQuote()
    {
        // act
        var markup = Parser.Parse("> before\n> ---\n> after");

        // assert
        var quote = markup.Should().BeOfType<BlockQuoteMarkup>().Subject;
        quote.Content.Should().BeOfType<MarkupSeq>()
            .Which.Items.Should().ContainSingle(x => x is DividerMarkup);
    }

    [Theory]
    [InlineData("--")]
    [InlineData("---x")]
    [InlineData("--- text")]
    [InlineData("a ---")]
    [InlineData("- --")]
    public void NotDividerShouldStayText(string text)
    {
        // act
        var markup = Parser.Parse(text);

        // assert
        markup.Should().NotBeOfType<DividerMarkup>();
        markup.Format().Should().Be(text);
    }

    [Fact]
    public void ListItemShouldStillParse()
    {
        // act
        var markup = Parser.Parse("- item");

        // assert
        markup.Should().BeOfType<ListMarkup>();
    }

    [Fact]
    public void TableDelimiterShouldStillParse()
    {
        // act
        var markup = Parser.Parse("| a | b |\n| --- | --- |\n| 1 | 2 |");

        // assert
        markup.Should().BeOfType<TableMarkup>();
    }

    [Fact]
    public void DividerShouldBeOmittedFromFlattenedAndSpokenText()
    {
        // arrange
        var markup = Parser.Parse("above\n---\nbelow");

        // act
        var readable = markup.ToReadableText();
        var spoken = markup.ToSpokenText();

        // assert
        readable.Should().Be("above below");
        spoken.Should().Be("above.\nbelow.");
    }

    [Fact]
    public void DividerShouldKeepItsTextInEditorHtml()
    {
        // arrange
        var markup = Parser.Parse("above\n---\nbelow");

        // act
        var html = MarkupEditorHtmlConverter.Instance.Format(markup);

        // assert
        html.Should().Contain("---");
    }

    [Fact]
    public void DividerShouldPassThroughMessagePack()
    {
        // arrange
        Markup markup = new DividerMarkup();

        // act
        var result = markup.PassThroughModernSerializers(Out);

        // assert
        result.Should().BeOfType<DividerMarkup>();
    }

    [Fact]
    public void DividerShouldNotBeFoundByLinkOrMentionExtractors()
    {
        // arrange
        var markup = Parser.Parse("---");

        // act & assert
        new LinkExtractor().GetLinks(markup).Should().BeEmpty();
        new MentionExtractor().GetMentionIds(markup).Should().BeEmpty();
    }
}

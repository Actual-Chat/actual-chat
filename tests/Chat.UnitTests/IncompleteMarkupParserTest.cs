namespace ActualChat.Chat.UnitTests;

public class IncompleteMarkupParserTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly IMarkupParser Parser = new MarkupParser { AllowIncompleteMarkup = true };
    private static readonly IMarkupParser FullParser = new MarkupParser();

    [Theory]
    [InlineData("Hello **bold", TextStyle.Bold, "bold")]
    [InlineData("Hello *ital", TextStyle.Italic, "ital")]
    [InlineData("Hello ||spoi", TextStyle.Spoiler, "spoi")]
    public void UnterminatedStyleIsMatchedAsIncomplete(string text, TextStyle style, string styledText)
    {
        // act
        var markup = Parser.Parse(text);

        // assert
        var seq = GetParagraphContent(markup).Should().BeOfType<MarkupSeq>().Subject;
        seq.Items[0].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be("Hello ");
        var stylized = seq.Items[1].Should().BeOfType<StylizedMarkup>().Subject;
        stylized.Style.Should().Be(style);
        stylized.IsIncomplete.Should().BeTrue();
        stylized.Content.ToReadableText().Should().Be(styledText);
    }

    [Fact]
    public void UnterminatedPreformattedIsMatchedAsIncomplete()
    {
        // act
        var markup = Parser.Parse("Hello `cod");

        // assert
        var seq = GetParagraphContent(markup).Should().BeOfType<MarkupSeq>().Subject;
        var pre = seq.Items[1].Should().BeOfType<PreformattedTextMarkup>().Subject;
        pre.Text.Should().Be("cod");
        pre.IsIncomplete.Should().BeTrue();
    }

    [Theory]
    [InlineData("Hello **bold**")]
    [InlineData("Hello *ital*")]
    [InlineData("Hello ||spoi||")]
    [InlineData("Hello `code`")]
    public void TerminatedMarkupIsNotIncomplete(string text)
    {
        // act
        var markup = Parser.Parse(text);

        // assert
        GetIncompleteItems(markup).Should().BeEmpty();
    }

    [Fact]
    public void OnlyTheTrailingSpanIsIncomplete()
    {
        // act
        var markup = Parser.Parse("Hello **bold** and *it");

        // assert
        var seq = GetParagraphContent(markup).Should().BeOfType<MarkupSeq>().Subject;
        seq.Items.OfType<StylizedMarkup>().Should().SatisfyRespectively(
            bold => {
                bold.Style.Should().Be(TextStyle.Bold);
                bold.IsIncomplete.Should().BeFalse();
            },
            italic => {
                italic.Style.Should().Be(TextStyle.Italic);
                italic.IsIncomplete.Should().BeTrue();
            });
    }

    [Fact]
    public void NestedUnterminatedSpansAreBothIncomplete()
    {
        // act
        var markup = Parser.Parse("**bold ||secret");

        // assert
        var bold = GetParagraphContent(markup).Should().BeOfType<StylizedMarkup>().Subject;
        bold.Style.Should().Be(TextStyle.Bold);
        bold.IsIncomplete.Should().BeTrue();
        var inner = bold.Content.Should().BeOfType<MarkupSeq>().Subject;
        var spoiler = inner.Items[^1].Should().BeOfType<StylizedMarkup>().Subject;
        spoiler.Style.Should().Be(TextStyle.Spoiler);
        spoiler.IsIncomplete.Should().BeTrue();
        spoiler.Content.ToReadableText().Should().Be("secret");
    }

    [Theory]
    [InlineData("Hello **bold*", TextStyle.Bold, "bold")]
    [InlineData("Hello ||secret|", TextStyle.Spoiler, "secret")]
    public void HalfArrivedClosingTokenIsConsumed(string text, TextStyle style, string styledText)
    {
        // act
        var markup = Parser.Parse(text);

        // assert
        var seq = GetParagraphContent(markup).Should().BeOfType<MarkupSeq>().Subject;
        var stylized = seq.Items[^1].Should().BeOfType<StylizedMarkup>().Subject;
        stylized.Style.Should().Be(style);
        stylized.IsIncomplete.Should().BeTrue();
        stylized.Content.ToReadableText().Should().Be(styledText);
    }

    [Fact]
    public void SpoilerWithHalfArrivedClosingTokenStillHidesItsContent()
    {
        // The riskiest frame: "||secret|" must not fall back to literal text and expose the secret.

        // act
        var readable = Parser.Parse("Ending: ||the butler|").ToReadableText();

        // assert
        readable.Should().NotContain("butler");
    }

    [Fact]
    public void IncompleteSpoilerStillHidesItsContent()
    {
        // A spoiler must be masked the moment it opens - not once its closing token arrives.

        // act
        var readable = Parser.Parse("Ending: ||the butler did i").ToReadableText();

        // assert
        readable.Should().NotContain("butler");
        readable.Should().Contain(StylizedMarkup.SpoilerMaskChar.ToString());
    }

    [Theory]
    [InlineData("Hello **")]
    [InlineData("Hello *")]
    [InlineData("Hello ||")]
    public void OpeningTokenWithNoContentStaysLiteral(string text)
    {
        // An incomplete span needs non-empty content, otherwise a nested empty match would consume
        // the closing token of the span enclosing it - so a bare trailing token is still text.

        // act
        var markup = Parser.Parse(text);

        // assert
        GetIncompleteItems(markup).Should().BeEmpty();
        markup.ToReadableText().Should().Be(text);
    }

    [Theory]
    [InlineData("a ** b")]
    [InlineData("a * b")]
    [InlineData("a || b")]
    [InlineData("2 * 3 * 4")]
    public void TokensThatCannotReachEndOfInputStayLiteral(string text)
    {
        // An incomplete span must run to the end of the input, so a token mid-text is still text.

        // act
        var markup = Parser.Parse(text);

        // assert
        GetIncompleteItems(markup).Should().BeEmpty();
        markup.ToReadableText().Should().Be(text);
    }

    [Fact]
    public void IncompleteMarkupFormatsWithoutItsClosingToken()
    {
        // Format has to round-trip back to the received prefix, not invent the closing token.

        // act
        var markup = Parser.Parse("Hello **bold");

        // assert
        markup.Format().Should().Be("Hello **bold");
    }

    [Fact]
    public void UnclosedCodeBlockStaysACodeBlock()
    {
        // act
        var markup = Parser.Parse("```\nvar x = 1;");

        // assert
        markup.Should().BeOfType<CodeBlockMarkup>().Which.Code.Should().Be("var x = 1;");
    }

    [Fact]
    public void TokensInsideCodeSpansAndBlocksAreNotMatched()
    {
        // act
        var inSpan = Parser.Parse("Run `a ** b` now");
        var inBlock = Parser.Parse("```\nvar a = b ** c;\n```\nDone");

        // assert
        GetIncompleteItems(inSpan).Should().BeEmpty();
        GetIncompleteItems(inBlock).Should().BeEmpty();
        inSpan.Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<MarkupSeq>()
            .Which.Items[1].Should().BeOfType<PreformattedTextMarkup>()
            .Which.Text.Should().Be("a ** b");
    }

    [Theory]
    [InlineData("# Head")]
    [InlineData("> quo")]
    [InlineData("- item one\n- ite")]
    public void BlockConstructsParseTheSameAsWhenComplete(string text)
    {
        // act
        var readable = Parser.Parse(text).ToReadableText();

        // assert
        readable.Should().Be(FullParser.Parse(text).ToReadableText());
    }

    [Theory]
    [InlineData("Hello **bold** world")]
    [InlineData("A ||spoiler|| and *italics* and `code`")]
    [InlineData("Mixed **bold with ||spoiler|| inside** tail")]
    [InlineData("# Header\n\nA paragraph with **bold**.\n\n- one\n- two")]
    [InlineData("Text\n```\nvar x = 1;\n```\nAfter")]
    [InlineData("> quoted **bold**\n> more")]
    public void EveryPrefixParsesAndKeepsTextAPrefix(string text)
    {
        // No prefix may throw or render text the finished message doesn't start with.

        // arrange
        var fullText = ToComparableText(FullParser.Parse(text));

        for (var length = 1; length <= text.Length; length++) {
            // act
            var prefix = text[..length];
            var readable = ToComparableText(Parser.Parse(prefix));

            // assert
            fullText.Should().StartWith(readable,
                "prefix of length {0} ({1}) must render a prefix of the whole message", length, prefix);
        }
    }

    [Theory]
    [InlineData("Hello **bold")]
    [InlineData("Hello *ital")]
    [InlineData("Hello ||spoi")]
    [InlineData("Hello `cod")]
    [InlineData("a || b")]
    [InlineData("a ||b c")]
    [InlineData("**bold with `code")]
    [InlineData("Text\n```\nvar x = 1;\n```\nAfter")]
    public void FullParserNeverProducesIncompleteMarkup(string text)
    {
        // The flag is the contract that separates the two parsers: without it, nothing is incomplete.

        // act
        var markup = FullParser.Parse(text);

        // assert
        GetIncompleteItems(markup).Should().BeEmpty();
    }

    [Theory]
    [InlineData("a || b")]
    [InlineData("a ||b c")]
    [InlineData("Hello **bold")]
    [InlineData("2 * 3 * 4")]
    public void FullParserRendersUnterminatedMarkupLiterally(string text)
    {
        // act
        var readable = FullParser.Parse(text).ToReadableText();

        // assert
        readable.Should().Be(text);
    }

    // Private methods

    private static Markup GetParagraphContent(Markup markup)
        => markup.Should().BeOfType<ParagraphMarkup>().Subject.Content;

    private static List<Markup> GetIncompleteItems(Markup markup)
    {
        var items = new List<Markup>();
        Collect(markup);
        return items;

        void Collect(Markup m) {
            if (m.IsIncomplete)
                items.Add(m);
            switch (m) {
            case MarkupSeq seq:
                foreach (var item in seq.Items)
                    Collect(item);
                break;
            case StylizedMarkup stylized:
                Collect(stylized.Content);
                break;
            case ParagraphMarkup paragraph:
                Collect(paragraph.Content);
                break;
            case HeaderMarkup header:
                Collect(header.Content);
                break;
            case BlockQuoteMarkup blockQuote:
                Collect(blockQuote.Content);
                break;
            case ListMarkup list:
                foreach (var item in list.Items)
                    Collect(item);
                break;
            case ListItemMarkup listItem:
                Collect(listItem.Content);
                break;
            }
        }
    }

    private static string ToComparableText(Markup markup)
    {
        // Compares the content a reader sees, dropping markup tokens: a token is legitimately
        // literal until the span it opens can be matched, and spoiler content is masked either way.
        var text = markup.ToReadableText();
        var content = text.Where(c => c is not ('*' or '|' or '`' or StylizedMarkup.SpoilerMaskChar));
        return string.Join(' ', new string(content.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

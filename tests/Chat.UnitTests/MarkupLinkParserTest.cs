namespace ActualChat.Chat.UnitTests;

public class MarkupLinkParserTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly IMarkupParser Parser = new MarkupParser();
    private static readonly IMarkupParser IncompleteParser = new MarkupParser { AllowIncompleteMarkup = true };

    [Fact]
    public void TitledLinkShouldParseToUrlWithTitle()
    {
        // arrange
        const string text = "[Markdown Guide](https://www.markdownguide.org)";

        // act
        var markup = Parser.Parse(text);

        // assert
        var url = Content(markup).Should().BeOfType<UrlMarkup>().Subject;
        url.Url.Should().Be("https://www.markdownguide.org");
        url.Title.Should().Be("Markdown Guide");
        url.Kind.Should().Be(UrlMarkupKind.Www);
        markup.Format().Should().Be(text);
    }

    [Fact]
    public void TitledLinkShouldKeepSurroundingText()
    {
        // arrange
        const string text = "see [the guide](https://example.com/a?b=1) now";

        // act
        var markup = Parser.Parse(text);

        // assert
        var seq = Content(markup).Should().BeOfType<MarkupSeq>().Subject;
        seq.Items.Length.Should().Be(3);
        seq.Items[0].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be("see ");
        var url = seq.Items[1].Should().BeOfType<UrlMarkup>().Subject;
        url.Url.Should().Be("https://example.com/a?b=1");
        url.Title.Should().Be("the guide");
        seq.Items[2].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(" now");
        markup.Format().Should().Be(text);
    }

    [Fact]
    public void TitledLinkShouldAllowBalancedParenthesesInUrl()
    {
        // arrange
        const string text = "[Wiki](https://en.wikipedia.org/wiki/Foo_(bar)) is nice";

        // act
        var markup = Parser.Parse(text);

        // assert
        var seq = Content(markup).Should().BeOfType<MarkupSeq>().Subject;
        var url = seq.Items[0].Should().BeOfType<UrlMarkup>().Subject;
        url.Url.Should().Be("https://en.wikipedia.org/wiki/Foo_(bar)");
        url.Title.Should().Be("Wiki");
        markup.Format().Should().Be(text);
    }

    [Fact]
    public void TitledLinkShouldAcceptShortWwwUrl()
    {
        // act
        var markup = Parser.Parse("[Site](www.example.com/path)");

        // assert
        var url = Content(markup).Should().BeOfType<UrlMarkup>().Subject;
        url.Url.Should().Be("www.example.com/path");
        url.Title.Should().Be("Site");
        url.Kind.Should().Be(UrlMarkupKind.Www);
    }

    [Fact]
    public void TitledLinkToMailtoShouldBeEmailKind()
    {
        // act
        var markup = Parser.Parse("[Write me](mailto:john@example.com)");

        // assert
        var url = Content(markup).Should().BeOfType<UrlMarkup>().Subject;
        url.Url.Should().Be("mailto:john@example.com");
        url.Title.Should().Be("Write me");
        url.Kind.Should().Be(UrlMarkupKind.Email);
    }

    [Fact]
    public void TitledLinkShouldWorkInsideStyleHeaderListAndTable()
    {
        // arrange
        var texts = new[] {
            "**[Guide](https://example.com)**",
            "# Read [Guide](https://example.com)",
            "- item [Guide](https://example.com)",
            "| a |\n| --- |\n| [Guide](https://example.com) |",
        };

        foreach (var text in texts) {
            // act
            var markup = Parser.Parse(text);

            // assert
            new LinkExtractor().GetLinks(markup).Should().Equal(["https://example.com"], text);
            markup.Format().Replace("\r\n", "\n").Should().Be(text);
        }
    }

    [Fact]
    public void TitledLinkInsideCodeShouldStayText()
    {
        // act
        var markup = Parser.Parse("`[Guide](https://example.com)`");

        // assert
        Content(markup).Should().BeOfType<PreformattedTextMarkup>()
            .Which.Text.Should().Be("[Guide](https://example.com)");
        new LinkExtractor().GetLinks(markup).Should().BeEmpty();
    }

    [Theory]
    [InlineData("[text](not a url)")]
    [InlineData("[text](example)")]
    [InlineData("[text](javascript:alert(1))")]
    [InlineData("[](https://example.com)")]
    [InlineData("[text] (https://example.com)")]
    [InlineData("[text]")]
    [InlineData("[text](https://example.com")]
    [InlineData("[a\nb](https://example.com)")]
    public void NonLinkBracketsShouldStayText(string text)
    {
        // act
        var markup = Parser.Parse(text);

        // assert
        markup.Format().Replace("\r\n", "\n").Should().Be(text);
        FindUrls(markup).Should().NotContain(u => u.Title != null, "the brackets don't form a link");
    }

    [Fact]
    public void ExplicitLinkShouldParseToEnclosedUrl()
    {
        // arrange
        const string text = "<https://whatever>";

        // act
        var markup = Parser.Parse(text);

        // assert
        var url = Content(markup).Should().BeOfType<UrlMarkup>().Subject;
        url.Url.Should().Be("https://whatever");
        url.IsEnclosed.Should().BeTrue();
        url.Title.Should().BeNull();
        url.Kind.Should().Be(UrlMarkupKind.Www);
        markup.Format().Should().Be(text);
    }

    [Fact]
    public void ExplicitLinkShouldKeepSurroundingText()
    {
        // arrange
        const string text = "go to <https://example.com/a_b> now";

        // act
        var markup = Parser.Parse(text);

        // assert
        var seq = Content(markup).Should().BeOfType<MarkupSeq>().Subject;
        seq.Items[1].Should().BeOfType<UrlMarkup>().Which.Url.Should().Be("https://example.com/a_b");
        markup.Format().Should().Be(text);
    }

    [Fact]
    public void ExplicitLinkShouldAcceptEmail()
    {
        // act
        var markup = Parser.Parse("<john@example.com>");

        // assert
        var url = Content(markup).Should().BeOfType<UrlMarkup>().Subject;
        url.Url.Should().Be("john@example.com");
        url.Kind.Should().Be(UrlMarkupKind.Email);
        url.IsEnclosed.Should().BeTrue();
    }

    [Theory]
    [InlineData("a < b > c")]
    [InlineData("<b>bold</b>")]
    [InlineData("<not a url>")]
    [InlineData("<https://example.com")]
    [InlineData("1 <2 and 3> 0")]
    public void AngleBracketsThatAreNotLinksShouldStayText(string text)
    {
        // act
        var markup = Parser.Parse(text);

        // assert
        markup.Format().Should().Be(text);
        FindUrls(markup).Should().NotContain(u => u.IsEnclosed);
    }

    [Fact]
    public void LinksShouldBeFoundWhenTextHasNoOtherMarkup()
    {
        // The plain-text shortcut must not swallow a message whose only markup is a link
        // arrange
        var texts = new[] { "see [x](https://example.com)", "see <https://example.com>" };

        foreach (var text in texts) {
            // act
            var markup = Parser.Parse(text);

            // assert
            new LinkExtractor().GetLinks(markup).Should().Equal(["https://example.com"], text);
        }
    }

    [Fact]
    public void TitledLinkShouldReadAsTitleInFlattenedText()
    {
        // arrange
        var markup = Parser.Parse("open [the guide](https://example.com) please");

        // act
        var readable = markup.ToReadableText();
        var clipboard = markup.ToClipboardText();

        // assert
        readable.Should().Be("open the guide please");
        clipboard.Should().Be("open [the guide](https://example.com) please");
    }

    [Fact]
    public void TitledLinkShouldPassThroughMessagePack()
    {
        // arrange
        Markup markup = new UrlMarkup("https://example.com", UrlMarkupKind.Www) {
            Title = "Example",
            IsEnclosed = false,
        };

        // act
        var result = markup.PassThroughModernSerializers(Out);

        // assert
        var url = result.Should().BeOfType<UrlMarkup>().Subject;
        url.Url.Should().Be("https://example.com");
        url.Title.Should().Be("Example");
        url.Kind.Should().Be(UrlMarkupKind.Www);
    }

    [Fact]
    public void EnclosedLinkShouldPassThroughMessagePack()
    {
        // arrange
        Markup markup = new UrlMarkup("https://example.com", UrlMarkupKind.Www) { IsEnclosed = true };

        // act
        var result = markup.PassThroughModernSerializers(Out);

        // assert
        result.Should().BeOfType<UrlMarkup>().Which.IsEnclosed.Should().BeTrue();
    }

    [Fact]
    public void EnclosedLinkShouldReadWithoutBracketsInFlattenedAndSpokenText()
    {
        // arrange
        var web = Parser.Parse("see <https://example.com/a> now");
        var email = Parser.Parse("write to <john@example.com> please");

        // act & assert
        web.ToReadableText().Should().Be("see https://example.com/a now");
        email.ToReadableText().Should().Be("write to john@example.com please");
        email.ToSpokenText().Should().Be("write to john@example.com please.");
        web.ToClipboardText().Should().Be("see <https://example.com/a> now");
    }

    [Fact]
    public void LinkTitleShouldBeHtmlEncoded()
    {
        // arrange
        var markup = Parser.Parse("[<b>x</b> & y](https://example.com/?a=1&b=2)");

        // act
        var html = new TestHtmlFormatter().Format(markup);

        // assert
        html.Should().Contain("&lt;b&gt;x&lt;/b&gt;").And.NotContain("<b>");
        html.Should().Contain("href=\"https://example.com/?a=1&amp;b=2\"");
    }

    [Theory]
    [InlineData("see [Guide](", "see Guide")]
    [InlineData("see [Guide](h", "see Guide")]
    [InlineData("see [Guide](https://exa", "see Guide")]
    [InlineData("see [Guide](https://example.com", "see Guide")]
    [InlineData("see [Guide](https://x.y/f_(b", "see Guide")]
    [InlineData("see [Guide](https://x.y/f_(b)", "see Guide")]
    [InlineData("see [Guide](https://x.y/f_(b))", "see Guide")]
    [InlineData("see [Guide](https://example.com)", "see Guide")]
    [InlineData("see [Guide](https://example.com) now", "see Guide now")]
    public void HalfArrivedTitledLinkShouldShowItsTitleWhileStreaming(string prefix, string expected)
    {
        // act
        var markup = IncompleteParser.Parse(prefix);

        // assert
        markup.ToReadableText().Should().Be(expected);
    }

    [Theory]
    [InlineData("[a](not-a-url)")]
    [InlineData("[t](javascript:alert(1))")]
    [InlineData("[t](ftp)")]
    [InlineData("[a](not-a-url) and more")]
    public void ClosedNonLinkShouldStayLiteralWhileStreaming(string text)
    {
        // act
        var markup = IncompleteParser.Parse(text);

        // assert
        markup.ToReadableText().Should().Be(text);
    }

    [Fact]
    public void FinishedParagraphWithAClosedNonLinkShouldStayLiteralWhileStreaming()
    {
        // The end of a nested paragraph parse is not the end of the stream
        // act
        var markup = IncompleteParser.Parse("see [a](foo)\n\nnext");

        // assert
        markup.ToReadableText().Should().Contain("see [a](foo)");
    }

    [Fact]
    public void HalfArrivedTitledLinkShouldBeShownAsTextByTheCompleteParser()
    {
        // act
        var markup = Parser.Parse("see [Guide](https://exa");

        // assert
        markup.ToReadableText().Should().Be("see [Guide](https://exa");
    }

    [Fact]
    public void EveryStreamedPrefixOfALinkMessageShouldParse()
    {
        // arrange
        const string text = "go [Guide](https://example.com/a_(b)) or <https://example.com/c> and [x](mailto:a@b.co)!";

        // act & assert
        for (var length = 1; length <= text.Length; length++)
            FluentActions.Invoking(() => IncompleteParser.Parse(text[..length])).Should().NotThrow($"prefix {length}");
        IncompleteParser.Parse(text).Format().Should().Be(text);
    }

    [Theory]
    [MemberData(nameof(PathologicalInputs))]
    public void MalformedLinksShouldNotMakeTheParserSlow(string name, string text)
    {
        // act
        var task = Task.Run(() => Parser.Parse(text));
        var isCompleted = task.Wait(TimeSpan.FromSeconds(10).CiScaled());

        // assert
        isCompleted.Should().BeTrue($"{name} must parse in bounded time");
        task.Result.Format().Replace("\r\n", "\n").Should().Be(text);
    }

    [Fact]
    public void UrlMarkupShouldPassThroughEverySerializer()
    {
        // arrange
        var markup = new UrlMarkup("https://example.com", UrlMarkupKind.Www) { Title = "Example", IsEnclosed = true };

        // act & assert
        markup.AssertPassesThroughSerializers((result, original) => {
            result.Url.Should().Be(original.Url);
            result.Kind.Should().Be(original.Kind);
            result.Title.Should().Be("Example");
            result.IsEnclosed.Should().BeTrue();
        }, Out);
    }

    [Fact]
    public void UrlMarkupWithoutTitleShouldPassThroughEverySerializer()
    {
        // arrange
        var markup = new UrlMarkup("john@example.com", UrlMarkupKind.Email);

        // act & assert
        markup.AssertPassesThroughSerializers((result, original) => {
            result.Url.Should().Be(original.Url);
            result.Title.Should().BeNull();
            result.IsEnclosed.Should().BeFalse();
        }, Out);
    }

    public static TheoryData<string, string> PathologicalInputs()
        => new() {
            { "open brackets", new string('[', 20_000) },
            { "open links", string.Concat(Enumerable.Repeat("[a](", 5_000)) },
            { "open parens", "[a](" + new string('(', 20_000) },
            { "open angles", new string('<', 20_000) },
            { "huge enclosed url", "<https://" + new string('a', 60_000) },
            { "huge titled url", "[a](https://" + new string('a', 60_000) + ")" },
            { "huge title", "[" + new string('a', 60_000) + "](https://example.com)" },
        };

    // Private methods

    private sealed record TestHtmlFormatter : MarkupHtmlFormatterBase;

    private static Markup Content(Markup markup)
        => markup.Should().BeOfType<ParagraphMarkup>().Subject.Content;

    private static List<UrlMarkup> FindUrls(Markup markup)
        => markup switch {
            UrlMarkup url => [url],
            ParagraphMarkup p => FindUrls(p.Content),
            MarkupSeq seq => seq.Items.SelectMany(FindUrls).ToList(),
            _ => [],
        };
}

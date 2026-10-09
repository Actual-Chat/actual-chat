namespace ActualChat.Chat.UnitTests;

public sealed class MarkupUrlTrailingTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly IMarkupParser Parser = new MarkupParser();

    [Theory]
    [InlineData("(see https://x.com/a/b)", "(see ", "https://x.com/a/b", ")")]
    [InlineData("(see https://x.com)", "(see ", "https://x.com", ")")]
    [InlineData("[see https://x.com/a]", "[see ", "https://x.com/a", "]")]
    [InlineData("{see https://x.com/a}", "{see ", "https://x.com/a", "}")]
    [InlineData("https://x.com/a.", "", "https://x.com/a", ".")]
    [InlineData("https://x.com/a,", "", "https://x.com/a", ",")]
    [InlineData("https://x.com/a;", "", "https://x.com/a", ";")]
    [InlineData("https://x.com/a!", "", "https://x.com/a", "!")]
    [InlineData("www.x.com.", "", "www.x.com", ".")]
    [InlineData("(see https://x.com/a/b).", "(see ", "https://x.com/a/b", ").")]
    [InlineData("(see https://x.com/a_(b))", "(see ", "https://x.com/a_(b)", ")")]
    [InlineData("https://x.com/a_(b).", "", "https://x.com/a_(b)", ".")]
    public void BareUrlShouldNotIncludeTrailingPunctuationAndUnbalancedClosers(
        string text,
        string before,
        string expectedUrl,
        string after)
    {
        // arrange
        var expectedTexts = new[] { before, after }.Where(x => x.Length != 0).ToArray();

        // act
        var markup = Parser.Parse(text);

        // assert
        var items = Items(markup);
        items.OfType<UrlMarkup>().Should().ContainSingle().Which.Url.Should().Be(expectedUrl);
        items.OfType<PlainTextMarkup>().Select(x => x.Text).Should().Equal(expectedTexts);
        markup.Format().Should().Be(text);
    }

    [Theory]
    [InlineData("https://x.com/a_(b)")]
    [InlineData("https://en.wikipedia.org/wiki/Sampling_(signal_processing)")]
    [InlineData("https://x.com/a[0]")]
    [InlineData("https://x.com/?k='v'")]
    public void BareUrlShouldKeepBalancedClosers(string text)
    {
        // act
        var markup = Parser.Parse(text);

        // assert
        markup.Should().BeOfType<ParagraphMarkup>().Which.Content
            .Should().BeOfType<UrlMarkup>().Which.Url.Should().Be(text);
    }

    [Fact]
    public void TitledLinkShouldStayUnchanged()
    {
        // arrange
        const string text = "[t](https://x.com/a)";

        // act
        var markup = Parser.Parse(text);

        // assert
        var url = markup.Should().BeOfType<ParagraphMarkup>().Which.Content.Should().BeOfType<UrlMarkup>().Subject;
        url.Url.Should().Be("https://x.com/a");
        url.Title.Should().Be("t");
    }

    [Fact]
    public void EnclosedLinkShouldStayUnchanged()
    {
        // arrange
        const string text = "<https://x.com/a)>";

        // act
        var markup = Parser.Parse(text);

        // assert
        var url = markup.Should().BeOfType<ParagraphMarkup>().Which.Content.Should().BeOfType<UrlMarkup>().Subject;
        url.Url.Should().Be("https://x.com/a)");
        url.IsEnclosed.Should().BeTrue();
    }

    // Private methods

    private static Markup[] Items(Markup markup)
        => markup.Should().BeOfType<ParagraphMarkup>().Subject.Content switch {
            MarkupSeq seq => seq.Items.ToArray(),
            var content => [content],
        };
}

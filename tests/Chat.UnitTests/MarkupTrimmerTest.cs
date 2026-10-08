namespace ActualChat.Chat.UnitTests;

public class MarkupTrimmerTest
{
    [Fact]
    public void ShouldNotTrimShortMessages()
    {
        // arrange
        var markup = Markup.Join(new Markup[] {
            new PlainTextMarkup("Hi, "),
            TestAuthors.Jack.ToMentionMarkup(),
            new PlainTextMarkup("!"),
            new PlainTextMarkup(" How are you?"),
        });

        // act
        var trimmed = new MarkupTrimmer().Trim(markup, 100);

        // assert
        MarkupFormatter.ReadableUnstyled.Format(trimmed).Should().Be("Hi, @Jack! How are you?");
    }

    [Fact]
    public void ShouldTrimLongPlainText()
    {
        // arrange
        var markup = Markup.Join(new Markup[] {
            new PlainTextMarkup("Hi, Jack! How are you?"),
        });

        // act
        var trimmed = new MarkupTrimmer().Trim(markup, 10);

        // assert
        MarkupFormatter.ReadableUnstyled.Format(trimmed).Should().Be("Hi, Jack! …");
    }

    [Fact]
    public void ShouldChargeATitledLinkItsTitleNotItsTarget()
    {
        // arrange
        var markup = new MarkupParser().Parse("[go](https://example.com/a/very/long/path/that/goes/on) now");

        // act
        var trimmed = new MarkupTrimmer().Trim(markup, 20);

        // assert
        trimmed.ToReadableText().Should().Be("go now");
    }

    [Fact]
    public void ShouldTrimALongTitleOfAShortLink()
    {
        // arrange
        var markup = new MarkupParser().Parse($"[{new string('a', 200)}](https://x.co) tail");

        // act
        var trimmed = new MarkupTrimmer().Trim(markup, 20);

        // assert
        trimmed.ToReadableText().Should().NotContain("tail").And.EndWith("…");
    }

    [Fact]
    public void ShouldTrimMultiplePlainTextBlocks()
    {
        // arrange
        var markup = Markup.Join(new Markup[] {
            new PlainTextMarkup("Hi,"),
            new PlainTextMarkup(" "),
            new PlainTextMarkup("Jack!"),
            new PlainTextMarkup(" How "),
            new PlainTextMarkup("are you?"),
        });

        // act
        var trimmed = new MarkupTrimmer().Trim(markup, 11);

        // assert
        MarkupFormatter.ReadableUnstyled.Format(trimmed).Should().Be("Hi, Jack! H…");
    }
}

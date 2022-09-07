namespace ActualChat.Chat.UnitTests;

public class MarkupTrimmerTest
{
    [Fact]
    public void ShouldNotTrimShortMessages()
    {
        // arrange
        var markup = Markup.Join(new Markup[] {
            new PlainTextMarkup("Hi, "),
            new Mention("h123", "Jack"),
            new PlainTextMarkup("!"),
            new PlainTextMarkup(" How are you?"),
        });

        // act
        var trimmed = new MarkupTrimmer(100).Rewrite(markup);

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
        var trimmed = new MarkupTrimmer(10).Rewrite(markup);

        // assert
        MarkupFormatter.ReadableUnstyled.Format(trimmed).Should().Be("Hi, Jack! …");
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
        var trimmed = new MarkupTrimmer(11).Rewrite(markup);

        // assert
        MarkupFormatter.ReadableUnstyled.Format(trimmed).Should().Be("Hi, Jack! H…");
    }
}

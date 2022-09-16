namespace ActualChat.Chat.UnitTests;

public class MarkupFormatterTest
{
    [Fact]
    public void UnstyledShouldHandleMention()
    {
        // arrange
        var markup = Markup.Join(new Markup[] {
            new PlainTextMarkup("Hello, "),
            new MentionMarkup("h123", "Jack"),
            new PlainTextMarkup("!"),
        });

        // act
        var formatted = MarkupFormatter.ReadableUnstyled.Format(markup);

        // assert
        formatted.Should().Be("Hello, @Jack!");
    }
}

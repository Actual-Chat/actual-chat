namespace ActualChat.Chat.UnitTests;

public class MarkupFormatterTest
{
    [Fact]
    public void UnstyledShouldHandleMention()
    {
        // arrange
        var markup = Markup.Join(new Markup[] {
            new PlainTextMarkup("Hello, "),
            TestAuthors.Jack.ToMentionMarkup(),
            new PlainTextMarkup("!"),
        });

        // act
        var formatted = MarkupFormatter.ReadableUnstyled.Format(markup);

        // assert
        formatted.Should().Be("Hello, @Jack!");
    }
}

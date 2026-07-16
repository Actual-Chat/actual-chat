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

    [Fact]
    public void SpoilerShouldBeMaskedWhenUnstyled()
    {
        // arrange
        var markup = MarkupParser.ParseRaw("secret: ||hi there||").Simplify();

        // act
        var formatted = MarkupFormatter.ReadableUnstyled.Format(markup);

        // assert
        formatted.Should().Be("secret: ██ █████");
    }

    [Fact]
    public void SpoilerShouldRoundTripWhenStyled()
    {
        // arrange
        var markup = MarkupParser.ParseRaw("||hi there||").Simplify();

        // act & assert
        MarkupFormatter.Default.Format(markup).Should().Be("||hi there||");
        markup.Format().Should().Be("||hi there||");
    }

    [Fact]
    public void SpoilerShouldMaskMentionInside()
    {
        // arrange
        var markup = new StylizedMarkup(TestAuthors.Jack.ToMentionMarkup(), TextStyle.Spoiler);

        // act
        var formatted = MarkupFormatter.ReadableUnstyled.Format(markup);

        // assert
        formatted.Should().Be("█████");
        formatted.Should().NotContain("Jack");
    }
}

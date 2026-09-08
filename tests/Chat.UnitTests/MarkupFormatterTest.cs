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
    public void EmojiMentionShouldReadAsGlyph()
    {
        // act & assert
        FormatEmoji(EmojiRef.New(Emojis.Clown)).Should().Be(Emojis.Clown.Symbol);
        // A custom variant's id is a slug, so only its substitute symbol is readable
        FormatEmoji(EmojiRef.New(Emojis.ClownGinger)).Should().Be(Emojis.ClownGinger.Symbol);
        FormatEmoji(EmojiRef.FromText("no-such-emoji")).Should().Be(":no-such-emoji:");
        return;

        static string FormatEmoji(EmojiRef emojiRef)
            => MarkupFormatter.ReadableUnstyled.Format(new EmojiMention(MentionRef.NewEmoji(emojiRef)));
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

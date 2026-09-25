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

    [Fact]
    public void SpokenShouldDropCodeBlockAndKeepProse()
    {
        // arrange
        var markup = MarkupParser.ParseRaw("Here is the fix:\n```cs\nvar x = 1;\n```\nTry it.").Simplify();

        // act
        var spoken = markup.ToSpokenText();

        // assert
        spoken.Should().Be("Here is the fix:\nTry it.", "code is read, not heard, and the prose around it still is");
    }

    [Fact]
    public void SpokenShouldOmitSpoiler()
    {
        // arrange
        var markup = MarkupParser.ParseRaw("the answer is ||42|| by the way").Simplify();

        // act
        var spoken = markup.ToSpokenText();

        // assert
        spoken.Should().Be("the answer is by the way.", "a voice can neither hide nor mask what is hidden");
    }

    [Fact]
    public void SpokenShouldReadListItemsAsSentences()
    {
        // arrange
        var markup = MarkupParser.ParseRaw("Plan:\n- buy milk\n- call mom!\n- sleep").Simplify();

        // act
        var spoken = markup.ToSpokenText();

        // assert
        spoken.Should().Be("Plan:\nbuy milk.\ncall mom!\nsleep.",
            "an item ends a sentence so the voice pauses, and a marker is never read");
    }

    [Fact]
    public void SpokenShouldDropTokensOfHeaderQuoteAndStyles()
    {
        // arrange
        var markup = MarkupParser.ParseRaw("# Title\n> quoted\n**bold** and *italic*").Simplify();

        // act
        var spoken = markup.ToSpokenText();

        // assert
        spoken.Should().Be("Title.\nquoted.\nbold and italic.");
    }

    [Fact]
    public void SpokenShouldReadInlineCodeWithoutBackticks()
    {
        // arrange
        var markup = MarkupParser.ParseRaw("run `npm test` now").Simplify();

        // act
        var spoken = markup.ToSpokenText();

        // assert
        spoken.Should().Be("run npm test now.");
    }

    [Fact]
    public void SpokenShouldOmitTablesAndLinks()
    {
        // arrange
        var markup = MarkupParser.ParseRaw(
            "docs: https://example.com/a/b (read it)\n| a | b |\n| --- | --- |\n| 1 | 2 |").Simplify();

        // act
        var spoken = markup.ToSpokenText();

        // assert
        spoken.Should().Be("docs: (read it).", "a link or a table is nothing to listen to");
    }

    [Fact]
    public void SpokenShouldReadMentionAsName()
    {
        // arrange
        var markup = Markup.Join(new Markup[] {
            new PlainTextMarkup("Hello, "),
            TestAuthors.Jack.ToMentionMarkup(),
            new PlainTextMarkup("!"),
        });

        // act
        var spoken = markup.ToSpokenText();

        // assert
        spoken.Should().Be("Hello, Jack!");
    }

    [Fact]
    public void SpokenShouldBeEmptyForCodeOnlyMessage()
    {
        // arrange
        var markup = MarkupParser.ParseRaw("```\nselect 1;\n```").Simplify();

        // act
        var spoken = markup.ToSpokenText();

        // assert
        spoken.Should().BeEmpty("nothing is left to say, so nothing is synthesized");
    }
}

namespace ActualChat.Chat.UnitTests;

public class EmojiNormalizerTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly string Glyph = Emojis.BySymbol.Keys.First();

    private static EmojiMention VanillaEmoji()
        => new(MentionRef.NewEmoji(EmojiRef.FromText(Glyph)));

    [Fact]
    public void InlineVanillaEmojiBecomesPlainText()
    {
        var markup = new MarkupSeq(new PlainTextMarkup("hi "), VanillaEmoji());
        var result = EmojiNormalizer.Instance.Apply(markup);
        var seq = result.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items.OfType<EmojiMention>().Should().BeEmpty();
        seq.Items.OfType<TextMarkup>().Select(t => t.Text).Should().Contain(Glyph);
    }

    [Fact]
    public void SoleEmojiMentionIsKept()
    {
        var emoji = VanillaEmoji();
        EmojiNormalizer.Instance.Apply(emoji).Should().BeSameAs(emoji);

        var para = new ParagraphMarkup(emoji);
        EmojiNormalizer.Instance.Apply(para).Should().BeSameAs(para);
    }

    [Fact]
    public void CustomSlugEmojiIsKept()
    {
        // A slug with no Unicode glyph can only render via its mention — keep it.
        var emoji = new EmojiMention(MentionRef.NewEmoji(EmojiRef.Parse("ginger-clown")));
        var markup = new MarkupSeq(new PlainTextMarkup("hi "), emoji);
        var result = EmojiNormalizer.Instance.Apply(markup);
        result.Should().BeOfType<MarkupSeq>().Subject
            .Items.OfType<EmojiMention>().Should().ContainSingle();
    }
}

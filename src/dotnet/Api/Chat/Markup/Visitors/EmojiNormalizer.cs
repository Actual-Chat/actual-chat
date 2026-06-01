namespace ActualChat.Chat;

/// <summary>
/// Persist-time rewriter: an inline emoji mention whose target is a standard Unicode glyph is
/// replaced with that plain glyph (portable, old-client-friendly). A lone emoji mention (the whole
/// message) and custom/slug emojis with no Unicode glyph stay mentions.
/// </summary>
public sealed record EmojiNormalizer : MarkupRewriter<Unit>
{
    public static readonly EmojiNormalizer Instance = new();

    public Markup Apply(Markup markup)
    {
        if (markup.IsSoleEmojiMention())
            return markup; // Keep — it renders large.

        var state = default(Unit);
        return Visit(markup, ref state);
    }

    protected override Markup VisitMention(MentionMarkup markup, ref Unit state)
        => markup is EmojiMention em && Emojis.BySymbol.ContainsKey(em.EmojiRef.Text)
            ? new PlainTextMarkup(em.EmojiRef.Text)
            : markup;

    protected override Markup VisitListItem(ListItemMarkup markup, ref Unit state)
    {
        var newContent = Visit(markup.Content, ref state);
        return newContent == markup.Content ? markup : new ListItemMarkup(newContent);
    }
}

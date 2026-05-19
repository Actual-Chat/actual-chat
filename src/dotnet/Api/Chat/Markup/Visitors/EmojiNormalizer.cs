namespace ActualChat.Chat;

/// <summary>
/// Replaces every <see cref="EmojiMention"/> whose slug resolves to a known unicode emoji
/// with a <see cref="PlainTextMarkup"/> carrying the glyph. Custom (non-unicode) emoji
/// mentions are left untouched.
/// </summary>
public sealed record EmojiNormalizer : MarkupRewriter<Unit>
{
    public static readonly EmojiNormalizer Instance = new();

    public Markup Apply(Markup markup)
    {
        var state = default(Unit);
        return Visit(markup, ref state);
    }

    protected override Markup VisitMention(MentionMarkup markup, ref Unit state)
    {
        if (markup is not EmojiMention emoji)
            return markup;

        // Only the "vanilla" form — where the id is the glyph itself — gets unwrapped to
        // plain text. Named/custom slugs (e.g. "clown-yellow") stay as mentions because
        // their id isn't directly renderable.
        return Emojis.BySymbol.ContainsKey(emoji.EmojiRef.Text)
            ? new PlainTextMarkup(emoji.EmojiRef.Text)
            : markup;
    }

    protected override Markup VisitListItem(ListItemMarkup markup, ref Unit state)
    {
        var newContent = Visit(markup.Content, ref state);
        return newContent == markup.Content ? markup : new ListItemMarkup(newContent);
    }
}

namespace ActualChat.Chat;

public static class ChatMarkupHubExt
{
    public static async ValueTask<ChatEntry> NameMentions(
        this IChatMarkupHub markupHub,
        ChatEntry entry,
        CancellationToken cancellationToken)
    {
        if (!entry.HasMarkup)
            return entry;

        var content = entry.Content;
        if (content.IsNullOrEmpty())
            return entry;

        var markup = markupHub.Parser.Parse(content);
        var newMarkup = await markupHub.MentionNamer.Apply(markup, cancellationToken).ConfigureAwait(false);
        if (ReferenceEquals(newMarkup, markup))
            return entry;

        var newContent = MarkupFormatter.Default.Format(newMarkup);
        return entry with { Content = newContent };
    }

    public static Markup Parse(
        this IChatMarkupHub markupHub,
        string text)
    {
        var markup = markupHub.Parser.Parse(text);
        return markup;
    }

    public static async ValueTask<Markup> ParseAndNameMentions(
        this IChatMarkupHub markupHub,
        string text,
        CancellationToken cancellationToken)
    {
        var markup = markupHub.Parse(text);
        markup = await markupHub.MentionNamer.Apply(markup, cancellationToken).ConfigureAwait(false);
        return markup;
    }
}

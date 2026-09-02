namespace ActualChat.Chat;

public static class ChatMarkupHubExt
{
    public static ValueTask<Markup> GetMarkup(
        this IChatMarkupHub markupHub,
        ChatEntry entry,
        MarkupConsumer consumer,
        CancellationToken cancellationToken)
        => markupHub.GetMarkup(entry, null, consumer, cancellationToken);

    public static async ValueTask<Markup> GetMarkup(
        this IChatMarkupHub markupHub,
        ChatEntry entry,
        Translation? translation,
        MarkupConsumer consumer,
        CancellationToken cancellationToken)
    {
        Markup markup;
        switch (entry) {
        case SystemEntry systemEntry:
            markup = markupHub.SystemEntryMarkupBuilder.Build(systemEntry);
            // System entries render markup w/o mention names
            markup = await markupHub.MentionResolver.Apply(markup, cancellationToken).ConfigureAwait(false);
            break;
        case { HasAudio: true }:
            // HasAudio covers all audio/media entries now
            markup = new PlayableTextMarkup(translation?.Content ?? entry.Content, entry.Audio?.TimeMap ?? default);
            break;
        case { HasLocation: true }:
            // TODO: 2026-07, remove when all clients support location entries.
            // Their Content is a maps-link fallback baked in for old clients - no other consumer must show it.
            markup = markupHub.EmptyEntryMarkupBuilder.Build(entry, consumer);
            break;
        default:
            markup = markupHub.Parser.Parse(translation?.Content ?? entry.Content);
            if (ReferenceEquals(markup, MarkupParser.EmptyResult))
                markup = markupHub.EmptyEntryMarkupBuilder.Build(entry, consumer);
            break;
        }
        return markup;
    }

    public static Markup GetMarkup(
        this IChatMarkupHub markupHub,
        ChatEntry entry,
        MarkupConsumer consumer)
    {
        Markup markup;
        switch (entry) {
        case SystemEntry systemEntry:
            markup = markupHub.SystemEntryMarkupBuilder.Build(systemEntry);
            break;
        case { HasAudio: true }:
            // HasAudio covers all audio/media entries now
            markup = new PlayableTextMarkup(entry.Content, entry.Audio?.TimeMap ?? default);
            break;
        case { HasLocation: true }:
            // TODO: 2026-07, remove when all clients support location entries.
            // Their Content is a maps-link fallback baked in for old clients - no other consumer must show it.
            markup = markupHub.EmptyEntryMarkupBuilder.Build(entry, consumer);
            break;
        default:
            markup = markupHub.Parser.Parse(entry.Content);
            if (ReferenceEquals(markup, MarkupParser.EmptyResult))
                markup = markupHub.EmptyEntryMarkupBuilder.Build(entry, consumer);
            break;
        }
        return markup;
    }

    public static async ValueTask<string> PrepareForSave(
        this IChatMarkupHub markupHub,
        ChatEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.IsSystemEntry || entry.HasAudio)
            return entry.Content;

        var content = entry.Content;
        if (content.IsNullOrEmpty())
            return entry.Content;

        var markup = markupHub.Parser.Parse(content);
        var resolved = await markupHub.MentionResolver.Apply(markup, cancellationToken).ConfigureAwait(false);
        var normalized = MarkupNormalizer.Instance.Normalize(resolved);
        // Reformatting also rewrites code block indentation, so we do it only on an actual change.
        if (ReferenceEquals(normalized, markup))
            return entry.Content;

        return MarkupFormatter.Default.Format(normalized);
    }

    public static async ValueTask<Markup> Parse(
        this IChatMarkupHub markupHub,
        string markupText,
        bool mustNameMentions,
        CancellationToken cancellationToken)
    {
        var markup = markupHub.Parser.Parse(markupText);
        if (mustNameMentions)
            markup = await markupHub.MentionResolver.Apply(markup, cancellationToken).ConfigureAwait(false);
        return markup;
    }

    public static async ValueTask<Markup> ApplyMentionResolver(
        this IChatMarkupHub markupHub,
        Markup markup,
        CancellationToken cancellationToken)
        => await markupHub.MentionResolver.Apply(markup, cancellationToken).ConfigureAwait(false);
}

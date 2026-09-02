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
        var (markup, _) = await markupHub
            .GetMarkupWithSubstitution(entry, translation, consumer, cancellationToken)
            .ConfigureAwait(false);
        return markup;
    }

    // IsSubstituted says the text came from EmptyEntryMarkupBuilder rather than from the author, so
    // a caller composing for someone else must rebuild it in that reader's language.
    public static async ValueTask<(Markup Markup, bool IsSubstituted)> GetMarkupWithSubstitution(
        this IChatMarkupHub markupHub,
        ChatEntry entry,
        Translation? translation,
        MarkupConsumer consumer,
        CancellationToken cancellationToken)
    {
        switch (entry) {
        case SystemEntry systemEntry:
            var systemMarkup = markupHub.SystemEntryMarkupBuilder.Build(systemEntry);
            // System entries render markup w/o mention names
            systemMarkup = await markupHub.MentionResolver
                .Apply(systemMarkup, cancellationToken)
                .ConfigureAwait(false);
            return (systemMarkup, false);
        case { HasAudio: true }:
            // HasAudio covers all audio/media entries now
            var timeMap = entry.Audio?.TimeMap ?? default;
            return (new PlayableTextMarkup(translation?.Content ?? entry.Content, timeMap), false);
        case { HasLocation: true }:
            // TODO: 2026-07, remove when all clients support location entries.
            // Their Content is a maps-link fallback baked in for old clients - no other consumer must show it.
            return Substitute();
        default:
            var markup = markupHub.Parser.Parse(translation?.Content ?? entry.Content);
            if (!ReferenceEquals(markup, MarkupParser.EmptyResult))
                return (markup, false);

            return Substitute();
        }

        (Markup Markup, bool IsSubstituted) Substitute() {
            // MessageView and attachment-less entries come back empty - nothing was worded, so
            // there is nothing for a reader's language to change.
            var substitute = markupHub.EmptyEntryMarkupBuilder.Build(entry, consumer);
            return (substitute, !ReferenceEquals(substitute, Markup.EmptyText));
        }
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

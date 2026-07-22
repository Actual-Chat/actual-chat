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
            markup = systemEntry.ToMarkup();
            // System entries render markup w/o mention names
            markup = await markupHub.MentionResolver.Apply(markup, cancellationToken).ConfigureAwait(false);
            break;
        case { HasAudio: true }:
            // HasAudio covers all audio/media entries now
            markup = new PlayableTextMarkup(translation?.Content ?? entry.Content, entry.Audio?.TimeMap ?? default);
            break;
        default:
            markup = markupHub.Parser.Parse(translation?.Content ?? entry.Content);
            if (ReferenceEquals(markup, MarkupParser.EmptyResult))
                markup = GetEmptyMarkupReplacement(entry, consumer);
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
            markup = systemEntry.ToMarkup();
            break;
        case { HasAudio: true }:
            // HasAudio covers all audio/media entries now
            markup = new PlayableTextMarkup(entry.Content, entry.Audio?.TimeMap ?? default);
            break;
        default:
            markup = markupHub.Parser.Parse(entry.Content);
            if (ReferenceEquals(markup, MarkupParser.EmptyResult))
                markup = GetEmptyMarkupReplacement(entry, consumer);
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
        if (ReferenceEquals(resolved, markup))
            return entry.Content;

        return MarkupFormatter.Default.Format(resolved);
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

    // Private methods

    private static Markup GetEmptyMarkupReplacement(ChatEntry entry, MarkupConsumer consumer)
    {
        if (consumer is MarkupConsumer.MessageView)
            return Markup.EmptyText;

        if (entry.HasLocation) {
            var locationText = consumer is MarkupConsumer.ReactionNotification ? "location" : "a location";
            var locationPreamble = consumer is MarkupConsumer.ReactionNotification ? "your " : "Sent ";
            return new PlainTextMarkup(string.Concat(locationPreamble, locationText));
        }

        var attachments = entry.Attachments;
        if (attachments.Length == 0)
            return Markup.EmptyText;

        if (consumer is MarkupConsumer.QuoteView)
            return new PlainTextMarkup("Click to see the attachment");

        var imageCount = 0;
        var videoCount = 0;
        ChatEntryAttachment? firstFile = null;
        foreach (var x in attachments) // No LINQ to avoid boxing allocation
            if (x.IsSupportedImage())
                imageCount++;
            else if (x.IsSupportedVideo())
                videoCount++;
            else if (firstFile is null)
                firstFile = x;
        var fileCount = attachments.Length - imageCount - videoCount;

        var imageText = GetImageText();
        var videoText = GetVideoText();
        var fileText = GetFileText();

        var text = (imageText.Length, videoText.Length, fileText.Length) switch {
            (0, 0, _) => fileText,
            (0, _, 0) => videoText,
            (_, 0, 0) => imageText,
            (_, _, 0) => string.Concat(imageText, " and ", videoText),
            (_, 0, _) => string.Concat(imageText, " and ", fileText),
            (0, _, _) => string.Concat(videoText, " and ", fileText),
            _ => string.Concat(imageText, ", ", videoText, ", and ", fileText),
        };
        var preamble = consumer is MarkupConsumer.ReactionNotification ? "your " : "Sent ";
        return new PlainTextMarkup(string.Concat(preamble, text));

        string GetImageText()
            => imageCount switch {
                0 => "",
                1 => consumer is MarkupConsumer.ReactionNotification ? "image" : "an image",
                _ => $"{imageCount.Format()} images",
            };

        string GetVideoText()
            => videoCount switch {
                0 => "",
                1 => consumer is MarkupConsumer.ReactionNotification ? "video" : "a video",
                _ => $"{videoCount.Format()} videos",
            };

        string GetFileText()
            => fileCount switch {
                0 => "",
                1 => firstFile!.Media.FileName,
                _ => $"{fileCount.Format()} files",
            };
    }
}

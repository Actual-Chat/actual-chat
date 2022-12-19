using Cysharp.Text;

namespace ActualChat.Chat;

public static class ChatMarkupHubExt
{
    public static async ValueTask<Markup> GetMarkup(
        this IChatMarkupHub markupHub,
        ChatEntry entry,
        MarkupConsumer consumer,
        CancellationToken cancellationToken)
    {
        Markup markup;
        switch (entry) {
        case { SystemEntry: { } systemEntry }:
            markup = systemEntry.Option?.ToMarkup() ?? Markup.Empty;
            // System entries render markup w/o mention names
            markup = await markupHub.MentionNamer.Apply(markup, cancellationToken).ConfigureAwait(false);
            break;
        case { HasMediaEntry: true }:
            markup = new PlayableTextMarkup(entry.Content, entry.TextToTimeMap);
            break;
        default:
            markup = markupHub.Parser.Parse(entry.Content);
            if (ReferenceEquals(markup, Markup.Empty))
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
        case { SystemEntry: { } systemEntry }:
            markup = systemEntry.Option?.ToMarkup() ?? Markup.Empty;
            break;
        case { HasMediaEntry: true }:
            markup = new PlayableTextMarkup(entry.Content, entry.TextToTimeMap);
            break;
        default:
            markup = markupHub.Parser.Parse(entry.Content);
            if (ReferenceEquals(markup, Markup.Empty))
                markup = GetEmptyMarkupReplacement(entry, consumer);
            break;
        }
        return markup;
    }

    public static async ValueTask<ChatEntry> PrepareForSave(
        this IChatMarkupHub markupHub,
        ChatEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.IsSystemEntry || entry.HasMediaEntry)
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

    public static async ValueTask<Markup> Parse(
        this IChatMarkupHub markupHub,
        string markupText,
        bool mustNameMentions,
        CancellationToken cancellationToken)
    {
        var markup = markupHub.Parser.Parse(markupText);
        if (mustNameMentions)
            markup = await markupHub.MentionNamer.Apply(markup, cancellationToken).ConfigureAwait(false);
        return markup;
    }

    // Private methods

    private static Markup GetEmptyMarkupReplacement(ChatEntry entry, MarkupConsumer consumer)
    {
        if (consumer is MarkupConsumer.MessageView)
            return Markup.Empty;

        var attachments = entry.Attachments;
        if (attachments.Length == 0)
            return Markup.Empty;

        if (consumer is MarkupConsumer.QuoteView)
            return new PlainTextMarkup("Click to see attachment");

        var imageCount = 0;
        foreach (var x in attachments) // No LINQ to avoid boxing allocation
            if (x.IsImage())
                imageCount++;
        var fileCount = attachments.Length - imageCount;

        var imageText = imageCount switch {
            0 => "",
            1 => "an image",
            _ => $"{imageCount.ToString(CultureInfo.InvariantCulture)} images",
        };
        var fileText = fileCount switch {
            0 => "",
            1 => attachments[0].FileName,
            _ => $"{attachments.Length.ToString(CultureInfo.InvariantCulture)} files",
        };
        var text = (imageText.Length, fileText.Length) switch {
            (0, _) => fileText,
            (_, 0) => imageText,
            _ => ZString.Concat(imageText, " and ", fileText),
        };
        return new PlainTextMarkup(ZString.Concat("Sent ", text));
    }
}

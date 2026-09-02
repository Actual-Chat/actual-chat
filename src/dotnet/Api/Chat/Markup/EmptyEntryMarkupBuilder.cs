namespace ActualChat.Chat;

/// <summary>
/// Builds the markup shown for an entry carrying no text of its own - a shared location, or a
/// message that is only attachments. The words come from a subclass that can read a catalog;
/// this layer can't see one, so it owns the cases and nothing else.
/// </summary>
public abstract class EmptyEntryMarkupBuilder
{
    public const string LocationPin = "📍 ";
    // isLiveLocation isn't on the entry - it lives on the SharedLocation the entry points at, so
    // only a caller that resolved one can tell a live share from a one-shot pin.
    public Markup Build(ChatEntry entry, MarkupConsumer consumer, bool isLiveLocation = false)
    {
        if (consumer is MarkupConsumer.MessageView)
            return Markup.EmptyText;

        // The reaction line names what was reacted to ("❤️ to your image"), so it reads as a
        // noun phrase where every other consumer reads as a sentence.
        var isReaction = consumer is MarkupConsumer.ReactionNotification;
        if (entry.HasLocation) {
            if (isReaction)
                return new PlainTextMarkup(YourLocation);

            var locationText = isLiveLocation ? SentLiveLocation : SentLocation;
            // The chat list row draws a map-point icon beside its preview; everywhere else the text
            // is all there is, so the pin goes in here rather than into 21 catalogs.
            return new PlainTextMarkup(consumer is MarkupConsumer.ChatListItemText
                ? locationText
                : LocationPin + locationText);
        }

        var attachments = entry.Attachments;
        if (attachments.Length == 0)
            return Markup.EmptyText;
        if (consumer is MarkupConsumer.QuoteView)
            return new PlainTextMarkup(QuoteAttachment);

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

        // A mixed set collapses to a plain count: naming each kind means conjoining clauses,
        // and a conjunction whose parts must agree in case doesn't survive translation.
        var text = (imageCount, videoCount, fileCount) switch {
            ( > 0, 0, 0) => isReaction ? YourImages(imageCount) : SentImages(imageCount),
            (0, > 0, 0) => isReaction ? YourVideos(videoCount) : SentVideos(videoCount),
            (0, 0, 1) => isReaction ? YourFile(firstFile!.Media.FileName) : SentFile(firstFile!.Media.FileName),
            (0, 0, _) => isReaction ? YourFiles(fileCount) : SentFiles(fileCount),
            _ => isReaction ? YourAttachments(attachments.Length) : SentAttachments(attachments.Length),
        };
        return new PlainTextMarkup(text);
    }

    // Protected methods

    protected abstract string SentLocation { get; }
    protected abstract string SentLiveLocation { get; }
    protected abstract string YourLocation { get; }
    protected abstract string QuoteAttachment { get; }
    protected abstract string SentImages(int count);
    protected abstract string YourImages(int count);
    protected abstract string SentVideos(int count);
    protected abstract string YourVideos(int count);
    protected abstract string SentFile(string fileName);
    protected abstract string YourFile(string fileName);
    protected abstract string SentFiles(int count);
    protected abstract string YourFiles(int count);
    protected abstract string SentAttachments(int count);
    protected abstract string YourAttachments(int count);
}

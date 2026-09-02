namespace ActualChat.Chat;

// The English here is a deliberate second copy of the EmptyEntry_* keys in Strings.en.json:
// ActualChat.Api ships as a NuGet package and must word an entry without the catalog, which it
// can't reference. EmptyEntryLocalizationTest keeps the two copies equal.

/// <summary>
/// Builds the markup shown for an entry carrying no text of its own - a shared location, or a
/// message that is only attachments. The wording here is English; hosts that know the reader's
/// language override the per-case members with catalog values.
/// </summary>
public class EmptyEntryMarkupBuilder
{
    public const string LocationPin = "📍 ";
    public static readonly EmptyEntryMarkupBuilder Default = new();
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

    protected virtual string SentLocation => "Sent a location";
    protected virtual string SentLiveLocation => "Shared live location";
    protected virtual string YourLocation => "your location";
    protected virtual string QuoteAttachment => "Click to see the attachment";
    protected virtual string SentImages(int count) => $"Sent {count.Format()} image{Plural(count)}";
    protected virtual string YourImages(int count) => $"your image{Plural(count)}";
    protected virtual string SentVideos(int count) => $"Sent {count.Format()} video{Plural(count)}";
    protected virtual string YourVideos(int count) => $"your video{Plural(count)}";
    protected virtual string SentFile(string fileName) => $"Sent {fileName}";
    protected virtual string YourFile(string fileName) => $"your {fileName}";
    protected virtual string SentFiles(int count) => $"Sent {count.Format()} file{Plural(count)}";
    protected virtual string YourFiles(int count) => $"your file{Plural(count)}";
    protected virtual string SentAttachments(int count) => $"Sent {count.Format()} attachment{Plural(count)}";
    protected virtual string YourAttachments(int count) => $"your attachment{Plural(count)}";

    // Private methods

    private static string Plural(int count)
        // English-only, and that's the point: every other language reaches this text through
        // the catalog, where the forms are listed rather than derived.
        => count == 1 ? "" : "s";
}

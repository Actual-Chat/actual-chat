using Microsoft.Extensions.Localization;

namespace ActualChat.Localization;

/// <summary>
/// Words an entry with no text of its own in the reader's language. The UI resolves one from the
/// circuit; code composing text for someone else builds one per reader.
/// </summary>
public sealed class LocalizedEmptyEntryMarkupBuilder(IStringLocalizer l) : EmptyEntryMarkupBuilder
{
    private static readonly ConcurrentDictionary<Language, LocalizedEmptyEntryMarkupBuilder> Cache = new();

    // Keyed by language rather than by localizer: a circuit's localizer is scoped, and holding one
    // here would pin every circuit that ever rendered. The UI builds its own through DI instead.
    public static LocalizedEmptyEntryMarkupBuilder Get(Language language)
        => Cache.GetOrAdd(language,
            static x => new LocalizedEmptyEntryMarkupBuilder(LanguageStringLocalizer.Get(x)));

    // Protected methods

    protected override string SentLocation => l.EmptyEntry_SentLocation;
    protected override string SentLiveLocation => l.EmptyEntry_SentLiveLocation;
    protected override string YourLocation => l.EmptyEntry_YourLocation;
    protected override string QuoteAttachment => l.EmptyEntry_QuoteAttachment;
    protected override string SentImages(int count) => l.EmptyEntry_SentImages(count, count.Format());
    protected override string YourImages(int count) => l.EmptyEntry_YourImages(count);
    protected override string SentVideos(int count) => l.EmptyEntry_SentVideos(count, count.Format());
    protected override string YourVideos(int count) => l.EmptyEntry_YourVideos(count);
    protected override string SentFile(string fileName) => l.EmptyEntry_SentFile_Format(fileName);
    protected override string YourFile(string fileName) => l.EmptyEntry_YourFile_Format(fileName);
    protected override string SentFiles(int count) => l.EmptyEntry_SentFiles(count, count.Format());
    protected override string YourFiles(int count) => l.EmptyEntry_YourFiles(count);
    protected override string SentAttachments(int count) => l.EmptyEntry_SentAttachments(count, count.Format());
    protected override string YourAttachments(int count) => l.EmptyEntry_YourAttachments(count);
}

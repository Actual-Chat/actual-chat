using ActualChat.Hashing;
using MediaModel = ActualChat.Media.Media;

namespace ActualChat.Chat.UnitTests;

// Builds a ChatNews with every heavy field populated (attachments + media metadata,
// link preview, audio with time map, forward info) - the worst-case payload shape.
public static class ChatNewsTestData
{
    public static ChatNews CreateChatNews()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var entryId = ChatEntryId.New(chatId, 123);
        var beginsAt = new Moment(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc));
        var mediaId = MediaId.New("test", "media1");
        var thumbMediaId = MediaId.New("test", "media1thumb");
        var media = new MediaModel(mediaId) {
            BlobId = "blob-1",
            Version = 7,
            Kind = MediaKind.ChatEntryAttachment,
            Length = 12345L,
            FileName = "photo.jpg",
            ContentType = "image/jpeg",
            Width = 800,
            Height = 600,
        };
        var thumbMedia = new MediaModel(thumbMediaId) {
            BlobId = "blob-1-thumb",
            Version = 3,
            Kind = MediaKind.ChatEntryAttachment,
            Width = 80,
            Height = 60,
        };
        var linkPreview = new LinkPreview {
            Id = LinkPreview.ComposeId("https://example.com/page"),
            Version = 5,
            Url = "https://example.com/page",
            Title = "Example",
            Description = "Example page",
            CreatedAt = beginsAt,
            ModifiedAt = beginsAt,
            PreviewMediaId = mediaId,
            PreviewMedia = media,
            VideoWidth = 640,
            VideoHeight = 480,
            VideoUrl = "https://example.com/video",
            VideoSite = "YouTube",
        };
        var attachment = new ChatEntryAttachment("att-1", 2) {
            EntryId = entryId,
            Index = 0,
            MediaId = mediaId,
            ThumbnailMediaId = thumbMediaId,
            Media = media,
            ThumbnailMedia = thumbMedia,
        };
        var audio = new ChatEntryAudio {
            MediaId = mediaId,
            BlobId = "audio-blob",
            BeginsAt = beginsAt,
            EndsAt = beginsAt + TimeSpan.FromSeconds(10),
            ContentEndsAt = beginsAt + TimeSpan.FromSeconds(9),
            TimeMap = new LinearMap(0f, 0f, 1f, 1.5f),
        };
        var forwarded = new ChatEntryForwarded {
            ChatEntryId = ChatEntryId.New(chatId, 55),
            AuthorId = AuthorId.New(chatId, 21),
            BeginsAt = beginsAt,
            ChatTitle = "Source chat",
            AuthorName = "Bob",
        };
        var entry = new TextEntry(entryId, 42) {
            AuthorId = AuthorId.New(chatId, 10),
            BeginsAt = beginsAt,
            EndsAt = beginsAt + TimeSpan.FromSeconds(10),
            Content = "Hello https://example.com/page",
            ContentHash = ChatEntryHashExt.GetContentHashString("Hello https://example.com/page"),
            RepliedEntryLid = 100,
            Forwarded = forwarded,
            Audio = audio,
            LinkPreviewMode = LinkPreviewMode.Default,
            LinkPreviewIds = [linkPreview.Id],
            LinkPreviews = [linkPreview],
            Attachments = [attachment],
            HasReactions = true,
        };
        return new ChatNews(new Range<long>(1, 124), entry);
    }
}

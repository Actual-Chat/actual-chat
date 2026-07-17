using System.Security.Cryptography;
using ActualChat.Hashing;
using MediaModel = ActualChat.Media.Media;

namespace ActualChat.Chat.UnitTests;

// Guards the RemoteComputedCache hash-match optimization for IChats.GetNews:
// the server hashes the serialized result, so identical values must serialize
// to identical bytes across calls and processes.
public class ChatNewsSerializationDeterminismTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ChatNewsSerializationIsDeterministic()
    {
        // arrange
        var news = CreateChatNews();
        var s = MessagePackByteSerializer.Default;

        // act
        using var buffer1 = s.Write(news, typeof(ChatNews));
        var bytes1 = buffer1.WrittenSpan.ToArray();
        using var buffer2 = s.Write(news, typeof(ChatNews));
        var bytes2 = buffer2.WrittenSpan.ToArray();
        var copy = (ChatNews)s.Read(bytes1, typeof(ChatNews), out _)!;
        using var buffer3 = s.Write(copy, typeof(ChatNews));
        var bytes3 = buffer3.WrittenSpan.ToArray();
        var news2 = CreateChatNews();
        using var buffer4 = s.Write(news2, typeof(ChatNews));
        var bytes4 = buffer4.WrittenSpan.ToArray();

        // assert
        bytes2.Should().Equal(bytes1, "same instance must serialize to identical bytes");
        bytes3.Should().Equal(bytes1, "deserialize -> reserialize must produce identical bytes");
        bytes4.Should().Equal(bytes1, "identically-constructed instance must serialize to identical bytes");

        // Stable across processes? Run this test twice (separate runs) and compare the hash below.
        var hash = Convert.ToBase64String(SHA256.HashData(bytes1));
        Out.WriteLine($"ChatNews SHA256: {hash}");
        Out.WriteLine($"Length: {bytes1.Length}");
    }

    private static ChatNews CreateChatNews()
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

using ActualChat.Media;
using ActualChat.Uploads;

namespace ActualChat.Testing.Host;

public static class MediaOperations
{
    public static async Task<MediaId> SaveMedia(
        this IWebTester tester,
        ChatId chatId,
        UploadedFile file,
        CancellationToken cancellationToken = default)
    {
        var mediaSaver = tester.AppServices.GetRequiredService<IMediaSaver>();
        var mediaId = MediaId.New(chatId.Value);
        await mediaSaver.Save(mediaId, file, null, MediaKind.ChatEntryAttachment, cancellationToken);
        return mediaId;
    }

    public static async Task<MediaFull> CreateImageMedia(
        this IWebTester tester,
        ChatId chatId,
        string fileName = "image.png",
        string contentType = "image/png",
        int width = 800,
        int height = 600)
    {
        var mediaId = MediaId.New(chatId.Value);
        var media = new MediaFull(mediaId) {
            Kind = MediaKind.ChatEntryAttachment,
            BlobId = $"{mediaId.Value}/{fileName}",
            ContentType = contentType,
            FileName = fileName,
            Width = width,
            Height = height,
            Length = 1024,
        };
        var result = await tester.Commander.Call(new MediaBackend_Change(mediaId, null, Change.Create(media)), true);
        return result!;
    }

    public static async Task<MediaId> SaveTextFile(this IWebTester tester, ChatId chatId, string fileName, string content)
    {
        var testData = System.Text.Encoding.UTF8.GetBytes(content);
        var file = new UploadedStreamFile(
            fileName,
            "text/plain",
            testData.Length,
            () => Task.FromResult<Stream>(new MemoryStream(testData)));

        return await tester.SaveMedia(chatId, file);
    }
}

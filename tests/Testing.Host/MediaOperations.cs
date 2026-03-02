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

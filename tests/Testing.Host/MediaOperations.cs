using ActualChat.Uploads;

namespace ActualChat.Testing.Host;

public static class MediaOperations
{
    public static async Task<MediaId> Attach(
        this IWebClientTester tester,
        ChatId chatId,
        UploadedFile file,
        CancellationToken cancellationToken = default)
    {
        var mediaSaver = tester.AppServices.GetRequiredService<IMediaSaver>();
        var mediaId = MediaId.New(chatId.Value);
        await mediaSaver.Save(mediaId, file, null, cancellationToken);
        return mediaId;
    }
}

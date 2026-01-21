using ActualChat.Uploads;

namespace ActualChat.Testing.Host;

public static class MediaOperations
{
    public static Task<Media.Media> Attach(
        this IWebClientTester tester,
        ChatId chatId,
        UploadedFile file,
        CancellationToken cancellationToken = default)
        => tester.AppServices.GetRequiredService<IMediaSaver>().Save(chatId, file, null, cancellationToken);
}

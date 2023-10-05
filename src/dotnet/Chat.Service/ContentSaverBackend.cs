using Microsoft.IO;

namespace ActualChat.Chat;

public class ContentSaverBackend(IServiceProvider services) : IContentSaverBackend
{
    private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new();
    private IContentSaver ContentSaver { get; } = services.GetRequiredService<IContentSaver>();

    public virtual async Task SaveContent(ContentSaverBackend_SaveContent command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var stream = MemoryStreamManager.GetStream();
        await using var _ = stream.ConfigureAwait(false);
        await stream.WriteAsync(command.Content, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        var content = new Content(command.ContentId, command.ContentType, stream);
        await ContentSaver.Save(content, cancellationToken).ConfigureAwait(false);
    }
}

using Microsoft.IO;
using Storage.NetCore.Blobs;

namespace ActualChat.Chat;

public class ContentSaverBackend : IContentSaverBackend
{
    public const string ContentType = "Content-Type";
    private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new ();
    private IBlobStorage BlobStorage { get; }

    public ContentSaverBackend(IServiceProvider services)
        => BlobStorage = services.GetRequiredService<IBlobStorageProvider>().GetBlobStorage(BlobScope.ContentRecord);

    public virtual async Task SaveContent(IContentSaverBackend.SaveContentCommand command, CancellationToken cancellationToken)
    {
        var blobId = command.ContentId;
        var stream = MemoryStreamManager.GetStream();
        await using var _ = stream.ConfigureAwait(false);

        await stream.WriteAsync(command.Content, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        await BlobStorage.WriteAsync(blobId, stream, false, cancellationToken).ConfigureAwait(false);
        var blob = (await BlobStorage.GetBlobsAsync(new[] {blobId}, cancellationToken).ConfigureAwait(false)).Single();
        if (blob == null)
            throw new InvalidOperationException("Application invariant violated");
        blob.Metadata[ContentType] = command.ContentType;
        await BlobStorage.SetBlobsAsync(new[] {blob}, cancellationToken).ConfigureAwait(false);
    }
}

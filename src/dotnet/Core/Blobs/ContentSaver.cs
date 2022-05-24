using Storage.NetCore.Blobs;

namespace ActualChat.Blobs;

internal class ContentSaver : IContentSaver
{
    private readonly IBlobStorage _blobStorage;

    public ContentSaver(IBlobStorageProvider blobStorageProvider)
        => _blobStorage = blobStorageProvider.GetBlobStorage(BlobScope.ContentRecord);

    public async Task SaveContent(Content content, CancellationToken token)
    {
        await _blobStorage.WriteAsync(content.ContentId, content.Stream, false, token).ConfigureAwait(false);
        var blob = (await _blobStorage.GetBlobsAsync(new[] { content.ContentId }, token).ConfigureAwait(false)).Single();
        if (blob == null)
            throw new InvalidOperationException($"Unable to read stored blob: {content.ContentId}");

        blob.Metadata[Constants.Headers.ContentType] = content.ContentType;
        await _blobStorage.SetBlobsAsync(new[] { blob }, token).ConfigureAwait(false);
    }
}

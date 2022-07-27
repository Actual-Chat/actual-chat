using Storage.NetCore.Blobs;

namespace ActualChat.Blobs;

internal class ContentSaver : IContentSaver
{
    private readonly IBlobStorage _blobStorage;

    public ContentSaver(IBlobStorageProvider blobStorageProvider)
        => _blobStorage = blobStorageProvider.GetBlobStorage(BlobScope.ContentRecord);

    public async Task Save(Content content, CancellationToken cancellationToken)
    {
        await _blobStorage.WriteAsync(content.ContentId, content.Stream, false, cancellationToken)
            .ConfigureAwait(false);
        var blobs = await _blobStorage.GetBlobsAsync(new[] { content.ContentId }, cancellationToken)
            .ConfigureAwait(false);
        var blob = blobs.Single();
        if (blob == null)
            throw StandardError.NotFound<Blob>($"Unable to find blob #{content.ContentId}.");

        blob.Metadata[Constants.Headers.ContentType] = content.ContentType;
        await _blobStorage.SetBlobsAsync(new[] { blob }, cancellationToken).ConfigureAwait(false);
    }
}

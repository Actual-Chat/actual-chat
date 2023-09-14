namespace ActualChat.Blobs;

internal class ContentSaver(IBlobStorageProvider blobStorageProvider) : IContentSaver
{
    private readonly IBlobStorage _blobStorage = blobStorageProvider.GetBlobStorage(BlobScope.ContentRecord);

    public Task Save(Content content, CancellationToken cancellationToken)
        => _blobStorage.Write(content.ContentId, content.Stream, content.ContentType, cancellationToken);

    public Task Remove(string contentId, CancellationToken cancellationToken)
        => _blobStorage.Delete(contentId, cancellationToken);
}

namespace ActualChat.Blobs;

internal class ContentSaver(IBlobStorageProvider blobStorageProvider) : IContentSaver
{
    private readonly IBlobStorage _blobStorage = blobStorageProvider.GetBlobStorage(BlobScope.ContentRecord);

    public TimeSpan PostOperationDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    public async Task Save(Content content, CancellationToken cancellationToken)
    {
        await _blobStorage
            .Write(content.ContentId, content.Stream, content.ContentType, cancellationToken)
            .ConfigureAwait(false);
        await Task.Delay(PostOperationDelay, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task Remove(string contentId, CancellationToken cancellationToken)
    {
        await _blobStorage.Delete(contentId, cancellationToken).ConfigureAwait(false);
        await Task.Delay(PostOperationDelay, CancellationToken.None).ConfigureAwait(false);
    }
}

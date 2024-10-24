namespace ActualChat.Blobs;

public class ContentSaver(IBlobStorages blobStorages) : IContentSaver
{
    private readonly IBlobStorage _blobStorage = blobStorages[BlobScope.ContentRecord];

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

    public Task<bool> Exists(string contentId, CancellationToken cancellationToken)
        => _blobStorage.Exists(contentId, cancellationToken);

    public Task Copy(string sourceContentId, string destContentId, CancellationToken cancellationToken)
        => _blobStorage.Copy(sourceContentId, destContentId, cancellationToken);
}

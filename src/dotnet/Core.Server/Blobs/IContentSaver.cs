namespace ActualChat.Blobs;

public interface IContentSaver
{
    Task Save(Content content, CancellationToken cancellationToken);
    Task Remove(string contentId, CancellationToken cancellationToken);
    Task<bool> Exists(string contentId, CancellationToken cancellationToken);
    Task Copy(string sourceContentId, string destContentId, CancellationToken cancellationToken);
}

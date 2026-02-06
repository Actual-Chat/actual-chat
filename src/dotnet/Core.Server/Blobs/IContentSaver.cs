namespace ActualChat.Blobs;

/// <summary>
/// Service for saving, removing, and copying content in blob storage.
/// </summary>
public interface IContentSaver
{
    Task Save(Content content, CancellationToken cancellationToken);
    Task Remove(string contentId, CancellationToken cancellationToken);
    Task<bool> Exists(string contentId, CancellationToken cancellationToken);
    Task Copy(string sourceContentId, string destContentId, CancellationToken cancellationToken);
}

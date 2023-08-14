namespace ActualChat.Blobs;

public interface IContentSaver
{
    Task Save(Content content, CancellationToken cancellationToken);
    Task Remove(string contentId, CancellationToken cancellationToken);
}

namespace ActualChat.Blobs;

public interface IContentSaver
{
    Task SaveContent(Content content, CancellationToken token);
}

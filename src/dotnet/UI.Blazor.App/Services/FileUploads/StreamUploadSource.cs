namespace ActualChat.UI.Blazor.App.Services;

public class StreamUploadSource : IUploadSource
{
    public UploadSourceMetadata Metadata { get; }
    public Func<Task<Stream>> GetStream { get; }

    public StreamUploadSource(
        UploadSourceMetadata metadata,
        Func<Task<Stream>> getStream)
    {
        Metadata = metadata;
        GetStream = getStream;
    }
}

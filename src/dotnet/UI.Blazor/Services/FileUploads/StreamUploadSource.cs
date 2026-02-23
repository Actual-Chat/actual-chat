namespace ActualChat.UI.Blazor.Services;

public class StreamUploadSource : IUploadStreamSource
{
    public Func<Task<Stream>> GetStream { get; }

    public StreamUploadSource(Func<Task<Stream>> getStream)
        => GetStream = getStream;
}

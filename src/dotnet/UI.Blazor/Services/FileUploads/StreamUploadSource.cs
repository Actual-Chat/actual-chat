namespace ActualChat.UI.Blazor.Services;

public class StreamUploadSource(Func<Task<Stream>> getStream) : IUploadStreamSource
{
    public Func<Task<Stream>> GetStream { get; } = getStream;
}

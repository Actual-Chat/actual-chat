namespace ActualChat.UI.Blazor.Services;

public class StreamUploadSource(Func<Task<Stream>> streamFactory) : IUploadStreamSource
{
    public Task<Stream> GetStream()
        => streamFactory();
}

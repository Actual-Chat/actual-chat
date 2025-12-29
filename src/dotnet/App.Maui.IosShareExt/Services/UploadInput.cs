namespace ActualChat.App.Maui.IosShareExt.Services;

public record UploadInput(string ContentType, string FileName, Stream Stream) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
        => Stream.DisposeAsync();
}

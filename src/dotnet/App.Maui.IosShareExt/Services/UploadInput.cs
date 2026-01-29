namespace ActualChat.App.Maui.IosShareExt.Services;

public record UploadInput(string ContentType, string FileName, Disposable<Stream> Stream) : IDisposable
{
    public void Dispose()
        => Stream.DisposeSilently();
}

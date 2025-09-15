namespace ActualChat.App.Maui.Services.Recording;

public interface IAudioCapture
{
    IAsyncEnumerable<Memory<float>> Capture(CancellationToken cancellationToken);
}

namespace ActualChat.App.Maui.Services.Recording;

public interface IAudioCapture
{
    Task<IAsyncEnumerable<ReadOnlyMemory<float>>?> Capture(CancellationToken cancellationToken);
}

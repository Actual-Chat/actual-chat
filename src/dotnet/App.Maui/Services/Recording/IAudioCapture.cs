using System.Buffers;

namespace ActualChat.App.Maui.Services.Recording;

public interface IAudioCapture
{
    Task<IAsyncEnumerable<IMemoryOwner<float>>?> Capture(CancellationToken cancellationToken);
}

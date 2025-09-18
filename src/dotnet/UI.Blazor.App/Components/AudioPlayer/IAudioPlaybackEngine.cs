using ActualChat.Media;

namespace ActualChat.MediaPlayback;

public interface IAudioPlaybackEngine : IAsyncDisposable
{
    Task Play(CancellationToken cancellationToken);
    Task Pause(CancellationToken cancellationToken);
    Task Resume(CancellationToken cancellationToken);
    Task End(bool abort, CancellationToken cancellationToken);
    Task Frame(MediaFrame frame, CancellationToken cancellationToken);
}

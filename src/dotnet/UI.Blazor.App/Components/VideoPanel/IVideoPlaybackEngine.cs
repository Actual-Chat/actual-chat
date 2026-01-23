using ActualChat.Media;

namespace ActualChat.MediaPlayback;

public interface IVideoPlaybackEngine : IAsyncDisposable
{
    Task Play(CancellationToken cancellationToken);
    Task Stop(CancellationToken cancellationToken);
    ValueTask PushFrame(MediaFrame frame, CancellationToken cancellationToken);
}

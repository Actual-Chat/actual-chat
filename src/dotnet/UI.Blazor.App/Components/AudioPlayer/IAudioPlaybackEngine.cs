using ActualChat.Audio;

namespace ActualChat.MediaPlayback;

public interface IAudioPlaybackEngine : IAsyncDisposable
{
    Task Play(CancellationToken cancellationToken);
    Task Pause(CancellationToken cancellationToken);
    Task Resume(CancellationToken cancellationToken);
    Task End(bool mustAbort, CancellationToken cancellationToken);
    ValueTask PushFrame(AudioFrame frame, CancellationToken cancellationToken);
    ValueTask SkipUntil(TimeSpan sourceOffset, CancellationToken cancellationToken);
    ValueTask SpeedUpUntil(TimeSpan sourceOffset, int dropEveryNFrames, CancellationToken cancellationToken);
}

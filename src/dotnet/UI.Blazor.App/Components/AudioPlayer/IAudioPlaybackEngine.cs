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

    // Runtime playback-buffer target change (A/V-sync adaptive hold). Default
    // no-op so non-web engines keep their start-time hold; web overrides it.
    ValueTask SetTargetBufferSize(TimeSpan targetBufferSize, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}

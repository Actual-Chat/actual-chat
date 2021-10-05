using ActualChat.Media;

namespace ActualChat.Playback.Internal;

public abstract class MediaTrackPlayer
{
    public IMomentClock Clock { get; init; } = CpuClock.Instance;
    public MediaTrack Track { get; init; } = null!;
    public event Action<MediaFrame?, MediaFrame?>? Playing;

    public virtual async Task Play(CancellationToken cancellationToken)
    {
        await Clock.Delay(Track.StartAt, cancellationToken).ConfigureAwait(false);
        MediaFrame? prevFrame = null;
        await OnPlayStart().ConfigureAwait(false);
        try {
            var frames = Track.Source.Frames;
            await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                Playing?.Invoke(prevFrame, frame);
                prevFrame = frame;
                await OnPlayFrame(frame).ConfigureAwait(false);
            }
        }
        finally {
            Playing?.Invoke(prevFrame, null);
            await OnPlayStop().ConfigureAwait(false);
        }
    }

    protected abstract ValueTask OnPlayStart();
    protected abstract ValueTask OnPlayFrame(MediaFrame frame);
    protected abstract ValueTask OnPlayStop();
}

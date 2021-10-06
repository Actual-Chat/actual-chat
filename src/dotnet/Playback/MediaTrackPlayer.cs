using ActualChat.Media;

namespace ActualChat.Playback;

public abstract class MediaTrackPlayer
{
    public MediaTrack Track { get; init; } = null!;
    public event Action<PlayingMediaFrame?, PlayingMediaFrame?>? Playing;

    public virtual async Task Play(CancellationToken cancellationToken)
    {
        PlayingMediaFrame? prevFrame = null;
        await OnPlayStart().ConfigureAwait(false);
        try {
            var zeroTimestamp = Track.ZeroTimestamp;
            var frames = Track.Source.Frames;
            await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                var nextFrame = new PlayingMediaFrame(frame, zeroTimestamp + frame.Offset);
                Playing?.Invoke(prevFrame, nextFrame);
                prevFrame = nextFrame;
                await OnPlayNextFrame(nextFrame).ConfigureAwait(false);
            }
        }
        finally {
            Playing?.Invoke(prevFrame, null);
            await OnPlayStop().ConfigureAwait(false);
        }
    }

    protected abstract ValueTask OnPlayStart();
    protected abstract ValueTask OnPlayNextFrame(PlayingMediaFrame nextFrame);
    protected abstract ValueTask OnPlayStop();
}

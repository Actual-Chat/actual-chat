using ActualChat.Media;

namespace ActualChat.Playback.Internal;

public abstract class MediaChannelPlayer<TMediaChannel, TMediaFormat, TMediaFrame>
    where TMediaFormat : notnull
    where TMediaChannel : MediaChannel<TMediaFormat, TMediaFrame>
    where TMediaFrame : MediaFrame
{
    public TMediaChannel Channel { get; init; } = null!;
    public event Action<TMediaFrame?, TMediaFrame?>? Playing;

    public virtual async Task Play(CancellationToken cancellationToken)
    {
        TMediaFrame? prevFrame = null;
        await OnPlayStart().ConfigureAwait(false);
        try {
            var frames = Channel.Frames;
            while (await frames.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) {
                while (frames.TryRead(out var nextFrame)) {
                    Playing?.Invoke(prevFrame, nextFrame);
                    prevFrame = nextFrame;
                    await OnPlayFrame(nextFrame).ConfigureAwait(false);
                }
            }
        }
        finally {
            Playing?.Invoke(prevFrame, null);
            await OnPlayStop().ConfigureAwait(false);
        }
    }

    protected abstract ValueTask OnPlayStart();
    protected abstract ValueTask OnPlayFrame(TMediaFrame frame);
    protected abstract ValueTask OnPlayStop();
}

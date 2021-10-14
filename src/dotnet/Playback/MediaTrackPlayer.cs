namespace ActualChat.Playback;

public abstract class MediaTrackPlayer : AsyncProcessBase
{
    protected ILogger<MediaTrackPlayer> Log { get; init; }
    public MediaTrack Track { get; }
    public event Action<PlayingMediaFrame?, PlayingMediaFrame?>? Playing;

    protected MediaTrackPlayer(MediaTrack track, ILogger<MediaTrackPlayer> log)
    {
        Log = log;
        Track = track;
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        PlayingMediaFrame? prevFrame = null;
        try {
            await OnPlayStart().ConfigureAwait(false);
            var zeroTimestamp = Track.ZeroTimestamp;
            var frames = Track.Source.Frames;
            await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                var nextFrame = new PlayingMediaFrame(frame, zeroTimestamp + frame.Offset);
                Playing?.Invoke(prevFrame, nextFrame);
                prevFrame = nextFrame;
                await OnPlayNextFrame(nextFrame).ConfigureAwait(false);
            }
        }
        catch (TaskCanceledException) {
            throw;
        }
        catch (Exception ex) {
            Log.LogError(ex, "Failed to play media track");
        }
        finally {
            try {
                Playing?.Invoke(prevFrame, null);
                await OnPlayStop().ConfigureAwait(false);
            }
            finally {
                // AY: Sorry, but it's a self-disposing thing
                _ = DisposeAsync();
            }
        }
    }

    // Protected methods

    protected abstract ValueTask OnPlayStart();
    protected abstract ValueTask OnPlayNextFrame(PlayingMediaFrame nextFrame);
    protected abstract ValueTask OnPlayStop();
}

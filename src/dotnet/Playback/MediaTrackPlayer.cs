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
        Exception? error = null;
        try {
            await OnPlayStart().ConfigureAwait(false);
            var zeroTimestamp = Track.ZeroTimestamp;
            var frames = Track.Source.Frames;
            // var framesWithOffset = frames.SkipWhile(f => f.Offset < Track.Offset).SkipWhile(f => !f.IsKeyFrame);
            await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                var nextFrame = new PlayingMediaFrame(frame, zeroTimestamp + frame.Offset);
                Playing?.Invoke(prevFrame, nextFrame);
                prevFrame = nextFrame;
                await OnPlayNextFrame(nextFrame).ConfigureAwait(false);
            }
        }
        catch (TaskCanceledException) {
            // TODO(AK): this cancellation is requested unexpectedly during regular playback!!!!
            throw;
        }
        catch (Exception ex) {
            error = ex;
            Log.LogError(ex, "Failed to play media track");
        }
        finally {
            try {
                Playing?.Invoke(prevFrame, null);
                var stopImmediately = cancellationToken.IsCancellationRequested || error != null;
                await OnPlayStop(stopImmediately, cancellationToken).ConfigureAwait(false);
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
    protected abstract ValueTask OnPlayStop(bool stopImmediately, CancellationToken cancellationToken);
}

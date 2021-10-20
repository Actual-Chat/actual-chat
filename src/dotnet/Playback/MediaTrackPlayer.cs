namespace ActualChat.Playback;

public abstract class MediaTrackPlayer : AsyncProcessBase
{
    protected ILogger<MediaTrackPlayer> Log { get; init; }
    public MediaTrack Track { get; }

    public TimeSpan CurrentPlaybackTime { get; private set; }

    public event Action<PlayingMediaFrame?, PlayingMediaFrame?>? Playing;
    public event Action<TimeSpan>? PlaybackTimeChanged;

    protected MediaTrackPlayer(MediaTrack track, ILogger<MediaTrackPlayer> log)
    {
        Log = log;
        Track = track;
    }

    protected void RaisePlaybackTimeChanged(TimeSpan offset)
    {
        CurrentPlaybackTime = offset;
        PlaybackTimeChanged?.Invoke(CurrentPlaybackTime);
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        PlayingMediaFrame? prevFrame = null;
        Exception? error = null;
        try {
            await OnPlayStart(Track.Offset).ConfigureAwait(false);
            var zeroTimestamp = Track.ZeroTimestamp;
            var frames = Track.Source.Frames;
            await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                cancellationToken.ThrowIfCancellationRequested();

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
        catch (Exception ex) when (ex is not OperationCanceledException) {
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

    protected abstract ValueTask OnPlayStart(TimeSpan offset);
    protected abstract ValueTask OnPlayNextFrame(PlayingMediaFrame nextFrame);
    protected abstract ValueTask OnPlayStop(bool stopImmediately, CancellationToken cancellationToken);
}

using ActualChat.Media;

namespace ActualChat.Playback;

public abstract class MediaTrackPlayer : AsyncProcessBase
{
    private MediaTrackPlaybackState _state;

    protected ILogger<MediaTrackPlayer> Log { get; }
    protected IMediaSource Source { get; }

    public MediaTrackPlaybackState State => Volatile.Read(ref _state);
    public event Action<MediaTrackPlaybackState>? PlaybackStateChanged;

    protected MediaTrackPlayer(PlayMediaTrackCommand command, ILogger<MediaTrackPlayer> log)
    {
        Log = log;
        Source = command.Source;
        _state = new MediaTrackPlaybackState(command.TrackId, command.RecordingStartedAt);
        _ = Run();
    }

    public ValueTask EnqueueCommand(MediaTrackPlayerCommand command)
        => ProcessCommand(command);

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            await ProcessCommand(new StartPlaybackCommand(this)).ConfigureAwait(false);
            await foreach (var frame in Source.Frames.WithCancellation(cancellationToken).ConfigureAwait(false))
                await ProcessMediaFrame(frame, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            throw; // "Stop" is called, nothing to log here
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            error = ex;
            Log.LogError(ex, "Failed to play media track");
        }
        finally {
            var immediately = cancellationToken.IsCancellationRequested || error != null;
            var stopCommand = new StopPlaybackCommand(this, immediately);
            try {
                await ProcessCommand(stopCommand).ConfigureAwait(false);
            }
            finally {
                var state = Volatile.Read(ref _state);
                if (!state.IsCompleted) {
                    var stopped = state with { IsCompleted = true };
                    Volatile.Write(ref _state, stopped);
                    PlaybackStateChanged?.Invoke(stopped);
                }

                // AY: Sorry, but it's a self-disposing thing
                _ = DisposeAsync();
            }
        }
    }

    // Protected methods

    protected abstract ValueTask ProcessCommand(MediaTrackPlayerCommand command);
    protected abstract ValueTask ProcessMediaFrame(MediaFrame frame, CancellationToken cancellationToken);

    protected void OnPlayedTo(TimeSpan offset)
    {
        var state = Volatile.Read(ref _state) with { PlayingAt = offset };
        Volatile.Write(ref _state, state);

        PlaybackStateChanged?.Invoke(state);
    }

    protected void OnStopped(bool withError)
    {
        var state = Volatile.Read(ref _state) with { IsCompleted = true };
        Volatile.Write(ref _state, state);

        PlaybackStateChanged?.Invoke(state);
    }

    protected void OnVolumeSet(double volume)
    {
        var state = Volatile.Read(ref _state) with { Volume = volume };
        Volatile.Write(ref _state, state);

        PlaybackStateChanged?.Invoke(state);
    }
}

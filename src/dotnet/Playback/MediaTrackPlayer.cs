using ActualChat.Media;

namespace ActualChat.Playback;

public abstract class MediaTrackPlayer : AsyncProcessBase
{
    private volatile MediaTrackPlaybackState _state;
    private readonly object _stateLock = new();

    protected ILogger<MediaTrackPlayer> Log { get; }
    protected IMediaSource Source { get; }

    public MediaTrackPlaybackState State {
        // ReSharper disable once InconsistentlySynchronizedField
        get => _state;
        protected set {
            lock (_stateLock) {
                var lastState = _state;
                _state = value;
                StateChanged?.Invoke(lastState, value);
            }
        }
    }

    public event Action<MediaTrackPlaybackState, MediaTrackPlaybackState>? StateChanged;

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
                lock (_stateLock) {
                    if (!_state.IsCompleted)
                        OnStopped();
                }
                // AY: Sorry, but it's a self-disposing thing
                _ = DisposeAsync();
            }
        }
    }

    // Protected methods

    protected abstract ValueTask ProcessCommand(MediaTrackPlayerCommand command);
    protected abstract ValueTask ProcessMediaFrame(MediaFrame frame, CancellationToken cancellationToken);

    protected virtual void OnPlayedTo(TimeSpan offset)
        => UpdateState(offset, (o, s) => s with { PlayingAt = o });

    protected virtual void OnStopped(Exception? error = null)
        => UpdateState(s => s with { IsCompleted = true, Error = error });

    protected virtual void OnVolumeSet(double volume)
        => UpdateState(volume, (v, s) => s with { Volume = v });

    protected void UpdateState(Func<MediaTrackPlaybackState, MediaTrackPlaybackState> updater)
    {
        lock (_stateLock) {
            var lastState = _state;
            var state = updater.Invoke(lastState);
            _state = state;
            StateChanged?.Invoke(lastState, state);
        }
    }

    protected void UpdateState<TArg>(TArg arg, Func<TArg, MediaTrackPlaybackState, MediaTrackPlaybackState> updater)
    {
        lock (_stateLock) {
            var lastState = _state;
            var state = updater.Invoke(arg, lastState);
            _state = state;
            StateChanged?.Invoke(lastState, state);
        }
    }
}

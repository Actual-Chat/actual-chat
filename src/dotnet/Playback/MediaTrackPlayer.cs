using ActualChat.Media;

namespace ActualChat.Playback;

public abstract class MediaTrackPlayer : AsyncProcessBase
{
    private readonly object _stateLock = new ();
    private volatile MediaTrackPlaybackState _state;
    private readonly TaskSource<Unit> _whenCompletedSource;

    protected MomentClockSet Clocks { get; }
    protected ILogger<MediaTrackPlayer> Log { get; }

    public PlayMediaTrackCommand Command { get; }
    public IMediaSource Source => Command.Source;
    public Task WhenCompleted => _whenCompletedSource.Task;

    // ReSharper disable once InconsistentlySynchronizedField
    public MediaTrackPlaybackState State => _state;

    public event Action<MediaTrackPlaybackState, MediaTrackPlaybackState>? StateChanged;

    protected MediaTrackPlayer(
        PlayMediaTrackCommand command,
        MomentClockSet clocks,
        ILogger<MediaTrackPlayer> log)
    {
        Log = log;
        Clocks = clocks;
        Command = command;
        _state = new(command.TrackId, command.RecordingStartedAt, command.SkipTo);
        _whenCompletedSource = TaskSource.New<Unit>(true);
    }

    public ValueTask EnqueueCommand(MediaTrackPlayerCommand command)
        => ProcessCommand(command);

    // Protected methods

    protected abstract ValueTask ProcessCommand(MediaTrackPlayerCommand command);
    protected abstract ValueTask ProcessMediaFrame(MediaFrame frame, CancellationToken cancellationToken);

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            // Start delay
            if (Command.PlayAt.HasValue) {
                var playAt = Command.PlayAt.GetValueOrDefault();
                await Clocks.CpuClock.Delay(playAt, cancellationToken).ConfigureAwait(false);
            }

            // Actual playback
            await ProcessCommand(new StartPlaybackCommand(this)).ConfigureAwait(false);
            var frames = Source.GetFramesUntyped(cancellationToken);
            await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false))
                await ProcessMediaFrame(frame, cancellationToken).ConfigureAwait(false);
            await WhenCompleted.WithFakeCancellation(cancellationToken);
        }
        catch (OperationCanceledException) {
            throw; // "Stop" is called, nothing to log here
        }
        catch (Exception ex) {
            error = ex;
            Log.LogError(ex, "Failed to play media track");
        }
        finally {
            if (!WhenCompleted.IsCompleted) {
                var immediately = cancellationToken.IsCancellationRequested || error != null;
                var stopCommand = new StopPlaybackCommand(this, immediately);
                try {
                    await ProcessCommand(stopCommand).AsTask()
                        .WithTimeout(TimeSpan.FromSeconds(1), default)
                        .ConfigureAwait(false);
                    OnStopped();
                }
                catch (Exception e) {
                    OnStopped(e);
                }
            }
            // AY: It's a self-disposing thing
            _ = DisposeAsync();
        }
    }

    protected void UpdateState<TArg>(TArg arg, Func<TArg, MediaTrackPlaybackState, MediaTrackPlaybackState> updater)
    {
        lock (_stateLock) {
            var lastState = _state;
            if (lastState.IsCompleted)
                return; // No need to update it further
            var state = updater.Invoke(arg, lastState);
            if (lastState == state)
                return;
            _state = state;
            StateChanged?.Invoke(lastState, state);
            if (state.IsCompleted)
                _whenCompletedSource.TrySetResult(default);
        }
    }

    protected virtual void OnPlayedTo(TimeSpan offset)
        => UpdateState(offset, (o, s) => s with { IsStarted = true, PlayingAt = s.SkipTo + o });

    protected virtual void OnStopped(Exception? error = null)
        => UpdateState(error, (e, s) => s with { IsCompleted = true, Error = e });

    protected virtual void OnVolumeSet(double volume)
        => UpdateState(volume, (v, s) => s with { Volume = v });
}

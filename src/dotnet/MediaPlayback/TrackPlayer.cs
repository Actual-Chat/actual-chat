using ActualChat.Media;
using Stl.DependencyInjection;

namespace ActualChat.MediaPlayback;

public abstract class TrackPlayer : AsyncProcessBase, IHasServices
{
    private volatile TrackPlaybackState _state;
    private readonly TaskSource<Unit> _whenCompletedSource;

    protected ILogger Log { get; }
    protected ILogger? DebugLog => DebugMode ? Log : null;
    protected bool DebugMode { get; } = Constants.DebugMode.AudioPlayback;
    protected MomentClockSet Clocks { get; }

    public IServiceProvider Services { get; }
    public Playback Playback { get; }
    public PlayTrackCommand Command { get; }
    public IMediaSource Source => Command.Source;
    public Task WhenCompleted => _whenCompletedSource.Task;

    // ReSharper disable once InconsistentlySynchronizedField
    public TrackPlaybackState State => _state;

    public event Action<TrackPlaybackState, TrackPlaybackState>? StateChanged;

    protected TrackPlayer(Playback playback, PlayTrackCommand command)
    {
        Playback = playback;
        Services = Playback.Services;
        Log = Services.LogFor(GetType());
        Clocks = Services.Clocks();
        Command = command;
        _state = new(this);
        _whenCompletedSource = TaskSource.New<Unit>(true);
    }

    public ValueTask EnqueueCommand(TrackPlayerCommand command)
        => ProcessCommand(command);

    // Protected methods

    protected abstract ValueTask ProcessCommand(TrackPlayerCommand command);
    protected abstract ValueTask ProcessMediaFrame(MediaFrame frame, CancellationToken cancellationToken);

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        Exception? error = null;
        var isStarted = false;
        try {
            // Actual playback
            var frames = Source.GetFramesUntyped(cancellationToken);
            await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                if (!isStarted) {
                    // We do this here because we want to start buffering as early as possible
                    isStarted = true;
                    await Clocks.CpuClock.Delay(Command.PlayAt, cancellationToken).ConfigureAwait(false);
                    OnPlayedTo(TimeSpan.Zero);
                    await ProcessCommand(new StartPlaybackCommand(this)).ConfigureAwait(false);
                }
                await ProcessMediaFrame(frame, cancellationToken).ConfigureAwait(false);
            }
            await ProcessCommand(new StopPlaybackCommand(this, false)).ConfigureAwait(false);
            await WhenCompleted.WithFakeCancellation(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) {
            error = e;
            throw;
        }
        catch (Exception e) {
            error = e;
            Log.LogError(e, "Media track playback failed");
            throw;
        }
        finally {
            if (!WhenCompleted.IsCompleted) {
                var immediately = cancellationToken.IsCancellationRequested || error != null;
                var stopCommand = new StopPlaybackCommand(this, immediately);
                try {
                    await ProcessCommand(stopCommand).AsTask()
                        .WithTimeout(TimeSpan.FromSeconds(3), default)
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

    protected void UpdateState<TArg>(TArg arg, Func<TArg, TrackPlaybackState, TrackPlaybackState> updater)
    {
        TrackPlaybackState state;
        lock (Lock) {
            var lastState = _state;
            if (lastState.IsCompleted)
                return; // No need to update it further
            state = updater.Invoke(arg, lastState);
            if (lastState == state)
                return;
            _state = state;
            try {
                StateChanged?.Invoke(lastState, state);
            }
            catch (Exception e) {
                Log.LogError(e, "Error on StateChanged handler(s) invocation");
            }
        }
        if (state.IsCompleted)
            _whenCompletedSource.TrySetResult(default);
    }

    protected virtual void OnPlayedTo(TimeSpan offset)
        => UpdateState(offset, (o, s) => s with {
            IsStarted = true,
            PlayingAt = TimeSpanExt.Max(s.PlayingAt, s.Command.SkipTo + o),
        });

    protected virtual void OnStopped(Exception? error = null)
        => UpdateState(error, (e, s) => s with { IsCompleted = true, Error = e });

    protected virtual void OnVolumeSet(double volume)
        => UpdateState(volume, (v, s) => s with { Volume = v });
}

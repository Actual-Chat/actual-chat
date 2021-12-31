using ActualChat.Media;

namespace ActualChat.MediaPlayback;

public abstract class TrackPlayer : AsyncProcessBase, IHasServices
{
    private const int FrameDebugInfoPerEvery = 10;
    private const double MaxRealtimeDelay = 5d;

    private volatile TrackPlaybackState _state;
    private readonly TaskSource<Unit> _whenCompletedSource;
    private int _onPlayedToCallIndex;
    private ILogger? _log;

    protected ILogger Log => _log ??= Services.LogFor(GetType());
    protected ILogger? DebugLog => DebugMode ? Log : null;
    protected bool DebugMode => Constants.DebugMode.AudioPlayback;
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
        var delay = GetRealtimeDelay(TimeSpan.Zero);
        DebugLog?.LogDebug("Track #{TrackId}{Delay}: started, Command = {Command}", Command.TrackId, delay, Command);
        Exception? error = null;
        var isStarted = false;
        try {
            // Actual playback
            var frames = Source.GetFramesUntyped(cancellationToken);
            var frameIndex = 0;
            await foreach (var frame in frames.ConfigureAwait(false)) {
                if (!isStarted) {
                    delay = GetRealtimeDelay(TimeSpan.Zero);
                    DebugLog?.LogDebug("Track #{TrackId}{Delay}: first frame", Command.TrackId, delay);
                    // We do this here because we want to start buffering as early as possible
                    isStarted = true;
                    await Clocks.CpuClock.Delay(Command.PlayAt, cancellationToken).ConfigureAwait(false);
                    OnPlayedTo(TimeSpan.Zero);
                    delay = GetRealtimeDelay(TimeSpan.Zero);
                    DebugLog?.LogDebug("Track #{TrackId}{Delay}: StartPlaybackCommand", Command.TrackId, delay);
                    await ProcessCommand(new StartPlaybackCommand(this)).ConfigureAwait(false);
                }
                delay = GetRealtimeDelay(frame.Offset, frameIndex);
                if (delay.Length != 0)
                    DebugLog?.LogDebug("Track #{TrackId}{Delay}: ProcessMediaFrame", Command.TrackId, delay);
                await ProcessMediaFrame(frame, cancellationToken).ConfigureAwait(false);
                frameIndex++;
            }
            DebugLog?.LogDebug("Track #{TrackId}: StopPlaybackCommand", Command.TrackId);
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
    {
        UpdateState(offset, (o, s) => {
            var delay = GetRealtimeDelay(offset, _onPlayedToCallIndex++, 1);
            if (delay.Length != 0)
                DebugLog?.LogDebug("Track #{TrackId}{Delay}: OnPlayedTo({Offset})", Command.TrackId, delay, offset);
            return s with {
                IsStarted = true,
                PlayingAt = TimeSpanExt.Max(s.PlayingAt, o),
            };
        });
    }

    protected virtual void OnStopped(Exception? error = null)
        => UpdateState(error, (e, s) => s with { IsCompleted = true, Error = e });

    protected virtual void OnVolumeSet(double volume)
        => UpdateState(volume, (v, s) => s with { Volume = v });

    protected virtual string GetRealtimeDelay(TimeSpan frameOffset, int eventIndex = -1, int printEvery = FrameDebugInfoPerEvery)
    {
        if (DebugLog == null || (eventIndex > 0 && eventIndex % FrameDebugInfoPerEvery != 0))
            return "";
        var recordingTime = Command.RecordingStartedAt + frameOffset;
        var now = Clocks.SystemClock.Now;
        var realtimeDelay = (now - recordingTime).TotalSeconds;
        if (realtimeDelay > MaxRealtimeDelay)
            return "";
        var eventIndexFormatted = eventIndex >= 0 ? $" event #{eventIndex}" : "";
        return $"{eventIndexFormatted} delay={realtimeDelay * 1000:N1}ms (offset={frameOffset.TotalSeconds:N}s)";
    }
}

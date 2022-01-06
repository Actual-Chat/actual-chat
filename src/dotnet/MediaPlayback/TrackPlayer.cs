using ActualChat.Media;

namespace ActualChat.MediaPlayback;

public abstract class TrackPlayer : AsyncProcessBase, IHasServices
{
    private static readonly TimeSpan MaxReportedDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(1);

    private volatile TrackPlaybackState _state;
    private readonly TaskSource<Unit> _whenCompletedSource;
    private Moment _playbackDelayReportTime;
    private Moment _playedToDelayReportTime;
    private ILogger? _log;

    protected ILogger Log => _log ??= Services.LogFor(GetType());
    protected ILogger? DebugLog => DebugMode ? Log : null;
    protected bool DebugMode => Constants.DebugMode.AudioPlayback;
    protected readonly TimeSpan DelayReportPeriod = TimeSpan.FromSeconds(Constants.DebugMode.AudioPlayback ? 0.5 : 2);
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
        Log.LogInformation("Track #{TrackId}: started, Command={Command}, delay={Delay}",
            Command.TrackId, Command, GetDelayInfo(TimeSpan.Zero));
        Exception? error = null;
        var isStarted = false;
        try {
            // Actual playback
            var cpuClock = Clocks.CpuClock;
            var frames = Source.GetFramesUntyped(cancellationToken);
            await foreach (var frame in frames.ConfigureAwait(false)) {
                if (!isStarted) {
                    // We do this here because we want to start buffering as early as possible
                    isStarted = true;
                    var playbackDelay = Command.PlayAt - cpuClock.Now;
                    if (playbackDelay > TimeSpan.FromMilliseconds(10))
                        await cpuClock.Delay(playbackDelay, cancellationToken).ConfigureAwait(false);
                    OnPlayedTo(TimeSpan.Zero);
                    var startPlaybackTask = ProcessCommand(new StartPlaybackCommand(this));
                    Log.LogInformation("Track #{TrackId}: StartPlaybackCommand, delay={Delay}",
                        Command.TrackId, GetDelayInfo(TimeSpan.Zero));
                    await startPlaybackTask.ConfigureAwait(false);
                }
                var processMediaFrameTask = ProcessMediaFrame(frame, cancellationToken);
                GetDelayReportLog(ref _playbackDelayReportTime)
                    ?.LogInformation("Track #{TrackId}: ProcessMediaFrame, delay={Delay}",
                        Command.TrackId, GetDelayInfo(frame.Offset));
                await processMediaFrameTask.ConfigureAwait(false);
            }
            Log.LogInformation("Track #{TrackId}: StopPlaybackCommand", Command.TrackId);
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
                    await ProcessCommand(stopCommand).AsTask().WithTimeout(StopTimeout, default).ConfigureAwait(false);
                    await WhenCompleted.WithTimeout(StopTimeout, default).ConfigureAwait(false);
                    if (!WhenCompleted.IsCompleted)
                        OnStopped();
                }
                catch (Exception e) {
                    if (!WhenCompleted.IsCompleted)
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
        UpdateState((offset, this), static (arg, s) => {
            var (offset1, self) = arg;
            self.GetDelayReportLog(ref self._playedToDelayReportTime)
                ?.LogInformation("Track #{TrackId}: OnPlayedTo({Offset}), delay={Delay}",
                    self.Command.TrackId, offset1, self.GetDelayInfo(offset1));
            return s with {
                IsStarted = true,
                PlayingAt = TimeSpanExt.Max(s.PlayingAt, offset1),
            };
        });
    }

    protected virtual void OnStopped(Exception? error = null)
        => UpdateState(error, (e, s) => s with { IsCompleted = true, Error = e });

    protected virtual void OnVolumeSet(double volume)
        => UpdateState(volume, (v, s) => s with { Volume = v });


    // Delay reporting

    protected ILogger? GetDelayReportLog(ref Moment delayReportTime)
    {
        var now = Clocks.CpuClock.Now;
        if (now - delayReportTime < DelayReportPeriod)
            return null;
        delayReportTime = now;
        return Log;
    }

    protected string GetDelayInfo(TimeSpan frameOffset)
    {
        var recordedAt =  Command.TrackInfo.RecordedAt + frameOffset;
        var clientSideRecordedAt =  Command.TrackInfo.ClientSideRecordedAt + frameOffset;
        var now = Clocks.SystemClock.Now;
        var delay = now - recordedAt;
        if (delay > MaxReportedDelay)
            return "n/a";
        if (clientSideRecordedAt == recordedAt)
            return $"{delay.TotalMilliseconds:N1}ms @ {frameOffset.TotalSeconds:N}s";
        var clientSideDelay = now - clientSideRecordedAt;
        return $"{delay.TotalMilliseconds:N1}ms / {clientSideDelay.TotalMilliseconds:N1}ms @ {frameOffset.TotalSeconds:N}s";
    }
}

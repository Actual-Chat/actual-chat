using Microsoft.JSInterop;

namespace ActualChat.MediaPlayback;

/// <summary>
/// Event arguments for player state changes.
/// </summary>
public sealed record PlayerStateChangedEventArgs(PlayerState PreviousState, PlayerState State);

/// <summary>
/// Base class for playing audio tracks from a media source.
/// </summary>
public abstract class TrackPlayer(TrackInfo trackInfo, IMediaSource source, ILogger log) : ProcessorBase
{
    private readonly AsyncTaskMethodBuilder _whenCompletedSource = AsyncTaskMethodBuilderExt.New();
    private volatile Task? _whenPlaying;
    private volatile PlayerState _state = new();
    private readonly Lock _stateUpdateLock = new();
    private readonly Channel<IPlayerCommand> _commandQueue = Channel.CreateBounded<IPlayerCommand>(
        new BoundedChannelOptions(Constants.Queues.TrackPlayerCommandQueueSize) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

    protected TrackInfo TrackInfo { get; } = trackInfo;
    protected IMediaSource Source { get; } = source;
    protected CancellationTokenSource? PlayTokenSource;
    protected CancellationToken PlayToken;
    protected TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(3);
    protected ILogger Log { get; } = log;

    public PlayerState State => _state;
    public Task? WhenPlaying => _whenPlaying;
    public Task WhenCompleted => _whenCompletedSource.Task;
    public event Action<PlayerStateChangedEventArgs>? StateChanged;

    protected override async Task DisposeAsyncCore()
    {
        await Stop().ConfigureAwait(false);
        Source.Dispose();
    }

    /// <summary>
    /// Starts playing the track which is represented by <see cref="IMediaSource"/> (from ctor).
    /// </summary>
    /// <returns>A running task, which will be completed after playing all media frames
    /// or on a cancel + disposing things</returns>
    public Task Play(CancellationToken cancellationToken = default)
    {
        // Hint: the code here is almost a copy of WorkerBase.Run
        this.ThrowIfDisposedOrDisposing();

        lock (Lock) {
            if (_whenPlaying != null)
                throw StandardError.StateTransition(GetType(), "Play is already started.");
            this.ThrowIfDisposedOrDisposing();

            PlayTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, StopToken);
            PlayToken = PlayTokenSource.Token;

            var playStartingTask = OnPlayStarting(PlayToken);
            _whenPlaying = Task
                .Run(async () => {
                    try {
                        await playStartingTask.ConfigureAwait(false);
                        await PlayInternal(PlayToken).SilentAwait(false);
                    }
                    catch {
                        // Intended
                    }
                    finally {
                        PlayTokenSource.CancelAndDisposeSilently();
                        await OnPlayEnded().SilentAwait(false);
                    }
                }, CancellationToken.None);
#pragma warning disable MA0100
            return _whenPlaying;
#pragma warning restore MA0100
        }
    }

    /// <summary>
    /// Stops the playback.
    /// </summary>
    /// <returns>A running task which is completed when you can run
    /// <see cref="Play(CancellationToken)"/> again</returns>
    public Task Stop()
    {
        PlayTokenSource.CancelAndDisposeSilently();
        return WhenPlaying ?? Task.CompletedTask;
    }

    protected virtual Task OnPlayStarting(CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task OnPlayEnded() => Task.CompletedTask; // Should never fail!

    protected virtual async Task PlayInternal(CancellationToken cancellationToken)
    {
        Exception? exception = null;
        var playTask = ProcessCommand(PlayCommand.Instance, cancellationToken);
        var isPlayCommandProcessed = false;
        try {
            // We might send 0-20-40ms tracks, so the JS side should support this
            var frameCount = 0;
            var frames = Source.GetFramesUntyped(cancellationToken);
            await foreach (var frame in frames.ConfigureAwait(false)) {
                // An engine that already reported its end - errored, or its device died - would
                // otherwise be fed for as long as the source keeps producing.
                if (State.IsEnded)
                    break;

                if (!isPlayCommandProcessed) {
                    await playTask.ConfigureAwait(false);
                    isPlayCommandProcessed = true;
                }
                while (_commandQueue.Reader.TryRead(out var command))
                    await ProcessCommand(command, cancellationToken).ConfigureAwait(false);
                await ProcessMediaFrame(frame, cancellationToken).ConfigureAwait(false);
                frameCount++;
            }
            Log.LogDebug("Processed {FrameCount} frames for track {Id}", frameCount, TrackInfo.TrackId);

            // Note that end command shouldn't be cancelled with cancellationToken
            // this prevents sending (end + stop) commands simultaneously, don't change this.
            // change to get (end + stop) exists, for example, with a thread abort exception,
            // but it's a pretty rare situation
            // Bounded: it's the one engine call on the normal path that wasn't, and
            // Playback.OnAbortCommand awaits this task inline in its command loop.
            try {
                await ProcessCommand(EndCommand.Instance, CancellationToken.None).AsTask()
                    .WaitAsync(StopTimeout, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException) {
                Log.LogWarning("PlayInternal: the engine didn't finish End in {Timeout}, abandoning it",
                    StopTimeout.ToShortString());
            }

            // Now we're waiting for a report when the client side has actually played all frames or Cancel()
            // At the same time we need to pump commands queue in case pause or resume command arrive.
            while (true) {
                var readTask = _commandQueue.Reader.ReadAsync(cancellationToken).AsTask();
                var completedTask = await Task.WhenAny(readTask, WhenCompleted).ConfigureAwait(false);
                await completedTask.ConfigureAwait(false);
                if (completedTask == WhenCompleted)
                    break;
                var command = await readTask.ConfigureAwait(false);
                await ProcessCommand(command, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) {
            exception = ex;
            throw;
        }
        finally {
            // We should send a stop command & await it even if the thread is aborted,
            // that's why the exception handling is in the "finally" block
            if (exception != null && !WhenCompleted.IsCompleted) {
                var clock = MomentClockSet.Default.CpuClock;
                var stopTime = clock.Now + StopTimeout;
                try {
                    if (!isPlayCommandProcessed)
                        await playTask.AsTask()
                            .WaitAsync((stopTime - clock.Now).Positive(), CancellationToken.None)
                            .ConfigureAwait(false);
                    var abortResult = await ProcessCommand(AbortCommand.Instance, CancellationToken.None).AsTask()
                        .WaitResultAsync((stopTime - clock.Now).Positive(), CancellationToken.None)
                        .ConfigureAwait(false);
                    if (abortResult.HasError)
                        SetEndState(abortResult.Error);
                    await WhenCompleted
                        .WaitAsync((stopTime - clock.Now).Positive(), CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) {
                    if (ex is not JSDisconnectedException)
                        Log.LogError(ex, $"Unhandled exception in {nameof(TrackPlayer)} while sending Stop command");
                }
            }
        }
    }

    protected abstract ValueTask ProcessCommand(IPlayerCommand command, CancellationToken cancellationToken);
    protected abstract ValueTask ProcessMediaFrame(MediaFrame frame, CancellationToken cancellationToken);

    public async Task Pause()
        => await _commandQueue.Writer.WriteAsync(PauseCommand.Instance, default).ConfigureAwait(false);

    public async Task Resume()
        => await _commandQueue.Writer.WriteAsync(ResumeCommand.Instance, default).ConfigureAwait(false);

    protected void UpdateState<TArg>(Func<TArg, PlayerState, PlayerState> updater, TArg arg)
    {
        lock (_stateUpdateLock) {
            var lastState = _state;
            if (lastState.IsEnded)
                return; // No need to update it further

            var state = updater.Invoke(arg, lastState);
            if (lastState == state)
                return;

            _state = state;
            try {
                StateChanged?.Invoke(new(lastState, state));
            }
            catch (Exception ex) {
                Log.LogError(ex, "Error on StateChanged handler(state) invocation");
            }
            if (state.IsEnded) {
                Log.LogDebug("TrackPlayer for track {Id} ended", TrackInfo.TrackId);
                _whenCompletedSource.TrySetResult();
            }
        }
    }

    protected void SetPlaybackState(TimeSpan offset, bool isPaused)
        => UpdateState(static (arg, state) => {
                var (offset1, isPaused1) = arg;
                return state with {
                    IsStarted = true,
                    IsPaused = isPaused1,
                    PlayingAt = TimeSpanExt.Max(state.PlayingAt, offset1),
                };
            },
            (offset, isPaused));

    protected void SetEndState(Exception? exception = null)
        => UpdateState(static (exception, state) => state with { IsEnded = true, Error = exception }, exception);
}

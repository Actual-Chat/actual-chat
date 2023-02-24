using ActualChat.Media;
using Microsoft.JSInterop;

namespace ActualChat.MediaPlayback;

public record struct PlayerStateChangedEventArgs(PlayerState PreviousState, PlayerState State);

public abstract class TrackPlayer : ProcessorBase
{
    private readonly TaskSource<Unit> _whenCompletedSource;
    private volatile Task? _whenPlaying;
    private volatile PlayerState _state = new();
    private readonly object _stateUpdateLock = new();
    private readonly Channel<IPlayerCommand> _commandsQueue;

    protected IMediaSource Source { get; }
    protected CancellationTokenSource? PlayTokenSource;
    protected CancellationToken PlayToken;
    protected TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(3);
    protected ILogger Log { get; }

    public PlayerState State => _state;
    public Task? WhenPlaying => _whenPlaying;
    public Task WhenCompleted => _whenCompletedSource.Task;
    public event Action<PlayerStateChangedEventArgs>? StateChanged;

    protected TrackPlayer(IMediaSource source, ILogger log)
    {
        Log = log;
        Source = source;
        _whenCompletedSource = TaskSource.New<Unit>(true);
        _commandsQueue = Channel.CreateBounded<IPlayerCommand>(
            new BoundedChannelOptions(Constants.Queues.TrackPlayerCommandQueueSize) {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
    }

    protected override Task DisposeAsyncCore()
        => Stop();

    /// <summary>
    /// Starts playing the track which is represented by <see cref="IMediaSource"/> (from ctor).
    /// </summary>
    /// <returns>A running task, which will be completed after playing all media frames or on a cancel + disposing things</returns>
    public Task Play(CancellationToken cancellationToken = default)
    {
        // Hint: the code here is almost a copy of WorkerBase.Run
        this.ThrowIfDisposedOrDisposing();

        lock (Lock) {
            if (_whenPlaying != null)
                throw StandardError.StateTransition(GetType(), "Play is already started.");
            this.ThrowIfDisposedOrDisposing();

            using (ExecutionContext.SuppressFlow()) {
                PlayTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, StopToken);
                PlayToken = PlayTokenSource.Token;

                var playStartingTask = OnPlayStarting(PlayToken);
                _whenPlaying = Task
                    .Run(async () => {
                        await playStartingTask.ConfigureAwait(false);
                        await PlayInternal(PlayToken).ConfigureAwait(false);
                    }, CancellationToken.None)
                    .ContinueWith(async _ => {
                        PlayTokenSource.CancelAndDisposeSilently();
                        try {
                            await OnPlayEnded().ConfigureAwait(false);
                        }
                        catch {
                            // Intended
                        }
                    }, TaskScheduler.Default);
#pragma warning disable MA0100
                return _whenPlaying;
#pragma warning restore MA0100
            }
        }
    }

    /// <summary>
    /// Stops the playback.
    /// </summary>
    /// <returns>A running task which is completed when you can run <see cref="Play(CancellationToken)"/> again</returns>
    public Task Stop()
    {
        PlayTokenSource?.CancelAndDisposeSilently();
        return WhenPlaying ?? Task.CompletedTask;
    }

    protected virtual Task OnPlayStarting(CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task OnPlayEnded() => Task.CompletedTask;

    protected virtual async Task PlayInternal(CancellationToken cancellationToken)
    {
        Exception? exception = null;
        var playTask = ProcessCommand(PlayCommand.Instance, cancellationToken);
        var isPlayCommandProcessed = false;
        try {
            // We might send to JS side small tracks even like 20-40ms (or without frames at all),
            // track might be less than JS threshold, so JS side should support this
            var frames = Source.GetFramesUntyped(cancellationToken);
            await foreach (var frame in frames.ConfigureAwait(false).WithCancellation(cancellationToken)) {
                if (!isPlayCommandProcessed) {
                    await playTask.ConfigureAwait(false);
                    isPlayCommandProcessed = true;
                }
                while (_commandsQueue.Reader.TryRead(out var command))
                    await ProcessCommand(command, cancellationToken).ConfigureAwait(false);
                await ProcessMediaFrame(frame, cancellationToken).ConfigureAwait(false);
            }

            // Note that end command shouldn't be cancelled with cancellationToken
            // this prevents sending (end + stop) commands simultaneously, don't change this.
            // change to get (end + stop) exists for example with a thread abort exception,
            // but it's a pretty rare situation
            await ProcessCommand(EndCommand.Instance, CancellationToken.None).ConfigureAwait(false);

            // Now we're waiting for a report when the client side has actually played all frames or Cancel()
            // At the same time we need to pump commands queue in case pause or resume command arrive.
            while (true) {
                var readTask = _commandsQueue.Reader.ReadAsync(cancellationToken).AsTask();
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
            // We should send stop command & await it even if thread is aborted,
            // that's why the exception handling is in the finally block
            if (exception != null && !WhenCompleted.IsCompleted) {
                var clock = MomentClockSet.Default.CpuClock;
                var stopTime = clock.Now + StopTimeout;
                try {
                    if (!isPlayCommandProcessed)
                        await playTask.AsTask()
                            .WaitResultAsync((stopTime - clock.Now).Positive(), CancellationToken.None)
                            .ConfigureAwait(false);
                    var stopResult = await ProcessCommand(StopCommand.Instance, CancellationToken.None).AsTask()
                        .WaitResultAsync((stopTime - clock.Now).Positive(), CancellationToken.None)
                        .ConfigureAwait(false);
                    if (stopResult.HasError)
                        OnStopped(stopResult.Error);
                    await WhenCompleted
                        .WaitResultAsync((stopTime - clock.Now).Positive(), CancellationToken.None)
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
        => await _commandsQueue.Writer.WriteAsync(PauseCommand.Instance, default).ConfigureAwait(false);

    public async Task Resume()
        => await _commandsQueue.Writer.WriteAsync(ResumeCommand.Instance, default).ConfigureAwait(false);

    protected void UpdateState<TArg>(Func<TArg, PlayerState, PlayerState> updater, TArg arg)
    {
        PlayerState state;
        lock (_stateUpdateLock) {
            var lastState = _state;
            if (lastState.IsCompleted)
                return; // No need to update it further
            state = updater.Invoke(arg, lastState);
            if (lastState == state)
                return;
            _state = state;
            try {
                StateChanged?.Invoke(new(lastState, state));
            }
            catch (Exception ex) {
                Log.LogError(ex, "Error on StateChanged handler(state) invocation");
            }
            if (state.IsCompleted)
                _whenCompletedSource.TrySetResult(default);
        }
    }

    protected virtual void OnPlayedTo(TimeSpan offset) => UpdateState(static (arg, state) => {
        var (offset1, self) = arg;
        return state with {
            IsStarted = true,
            IsPaused = false,
            PlayingAt = TimeSpanExt.Max(state.PlayingAt, offset1),
        };
    }, (offset, this));

    protected virtual void OnPausedAt(TimeSpan offset) => UpdateState(static (arg, state) => {
        var (offset1, self) = arg;
        return state with {
            IsPaused = true,
            PlayingAt = TimeSpanExt.Max(state.PlayingAt, offset1),
        };
    }, (offset, this));

    protected virtual void OnStopped(Exception? exception = null)
        => UpdateState(static (exception, state) => state with { IsCompleted = true, Error = exception }, exception);
}

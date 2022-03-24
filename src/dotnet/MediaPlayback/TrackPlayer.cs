using ActualChat.Media;
using Microsoft.Extensions.Hosting;
using Microsoft.JSInterop;

namespace ActualChat.MediaPlayback;

public abstract class TrackPlayer : IAsyncDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);
    private readonly CancellationToken _applicationStopping;
    private int _isDisposed;
    private int _isPlaying;
    private Task? _playingTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly IMediaSource _source;
    private readonly ILogger<TrackPlayer> _log;

    private volatile PlayerState _state = new();
    /// <summary>
    /// Struct <see cref="TaskCompletionSource"/> which signals when an actual processing is completed.
    /// For example audio (or video) playing on js side is ended or is stopped by an exception.
    /// </summary>
    private TaskSource<bool>? _whenCompleted;
    private readonly object _locker = new();

    public PlayerState State => _state;
    /// <summary>
    /// Returns a <see cref="Task"/> which is completed when actual playing is completed.
    /// For example audio (or video) playing on js side is ended or is stopped by an exception.
    /// </summary>
    public Task Completed => _whenCompleted?.Task ?? Task.CompletedTask;

    public event Action<PlayerStateChangedEventArgs>? StateChanged;

    protected TrackPlayer(IHostApplicationLifetime lifetime, IMediaSource source, ILogger<TrackPlayer> log)
    {
        _applicationStopping = lifetime.ApplicationStopping;
        _log = log;
        _source = source;
    }

    /// <summary>
    /// Starts playing the track which is represented by <see cref="IMediaSource"/> (from ctor).
    /// </summary>
    /// <returns>A running task, which will be completed after playing all media frames or on a cancel + disposing things</returns>
    public Task Play(CancellationToken cancellationToken = default)
    {
        if (_isDisposed == 1)
            throw new ObjectDisposedException(nameof(TrackPlayer));

        if (Interlocked.CompareExchange(ref _isPlaying, 1, 0) == 1)
            throw new LifetimeException("Playing is already started.");
        _whenCompleted = TaskSource.New<bool>(runContinuationsAsynchronously: true);
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_applicationStopping, cancellationToken);
        _playingTask = PlayInternal(_cancellationTokenSource.Token)
            .ContinueWith(task => {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                var exception = task.Exception?.InnerException ?? task.Exception;
                if (exception != null)
                    _whenCompleted?.TrySetException(exception);
                else
                    _whenCompleted?.TrySetResult(default);
                OnStopped(exception);
                _whenCompleted = null;
                _playingTask = null;
                // should be the last action in the continuation
                if (Interlocked.CompareExchange(ref _isPlaying, 0, 1) == 0)
                    throw new LifetimeException("Trying to reset playing, while playing wasn't started.");
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return _playingTask;
    }

    protected abstract ValueTask ProcessCommand(IPlayerCommand command, CancellationToken cancellationToken);
    protected abstract ValueTask ProcessMediaFrame(MediaFrame frame, CancellationToken cancellationToken);

    protected internal virtual async Task PlayInternal(CancellationToken cancellationToken)
    {
        Exception? exception = null;

        var isStarted = false;
        var startTask = ProcessCommandWrapper(PlayCommand.Instance, cancellationToken);
        try {
            // we might send to js side small tracks even like 20-40ms (or without frames at all),
            // track might be less than js threshold, so js side should support this
            var frames = _source.GetFramesUntyped(cancellationToken);
            await foreach (var frame in frames.ConfigureAwait(false).WithCancellation(cancellationToken)) {
                if (!isStarted) {
                    await startTask.ConfigureAwait(false);
                    isStarted = true;
                }
                await ProcessMediaFrame(frame, cancellationToken).ConfigureAwait(false);
            }
            /// note, that end command shouldn't be cancelled with <param name="cancellationToken" />
            /// this prevents sending (end + stop) commands simultaneously, don't change this.
            /// change to get (end + stop) exists for example with a thread abort exception,
            /// but it's a pretty rare situation
            await ProcessCommandWrapper(EndCommand.Instance, default).ConfigureAwait(false);
            // now we're waiting for a report when the client side has actually played all frames or Cancel()
            await Completed.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) {
            exception = ex;
            throw;
        }
        finally {
            // we should send stop command & await it even if thread is aborted,
            // that's why the exception handling is in the finally block
            if (exception != null && !Completed.IsCompletedSuccessfully) {
                try {
                    if (!isStarted) {
                        await startTask.ConfigureAwait(false);
                        isStarted = true;
                    }
                    await ProcessCommand(StopCommand.Instance, default)
                        .AsTask()
                        .WaitAsync(StopTimeout, default)
                        .ConfigureAwait(false);
                    // try to wait for processing the stop command
                    await Completed.WaitAsync(StopTimeout, default).ConfigureAwait(false);
                }
                catch (Exception ex) {
                    if (ex is not JSDisconnectedException)
                        _log.LogError(ex, $"Unhandled exception in {nameof(TrackPlayer)}, while sending stop command");
                }
            }
        }

        async ValueTask ProcessCommandWrapper(IPlayerCommand command, CancellationToken cancellationToken)
        {
            try {
                await ProcessCommand(command, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch (Exception ex) {
                throw new ProcessingException($"Processing command {command.GetType().Name} is failed.", ex) {
                    Command = command,
                };
            }
        }
    }
    /// <summary>
    /// Stops the playing of a track.
    /// </summary>
    /// <returns>A running task which is completed when you can run <see cref="Play(CancellationToken)"/> again</returns>
    public Task Stop()
    {
        var playingTask = _playingTask;
        _cancellationTokenSource?.Cancel();
        return playingTask ?? Task.CompletedTask;
    }

    protected void UpdateState<TArg>(Func<TArg, PlayerState, PlayerState> updater, TArg arg)
    {
        PlayerState state;
        lock (_locker) {
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
                _log.LogError(ex, "Error on StateChanged handler(state) invocation");
            }
        }
        if (state.IsCompleted)
            _whenCompleted?.TrySetResult(default);
    }

    protected virtual void OnPlayedTo(TimeSpan offset) => UpdateState(static (arg, state) => {
        var (offset1, self) = arg;
        return state with {
            IsStarted = true,
            PlayingAt = TimeSpanExt.Max(state.PlayingAt, offset1),
        };
    }, (offset, this));

    protected virtual void OnStopped(Exception? exception = null) => UpdateState(static (exception, state) => {
        return state with { IsCompleted = true, Error = exception };
    }, exception);

    protected virtual async ValueTask DisposeAsyncCore()
    {
        var playing = Interlocked.Exchange(ref _playingTask, null);
        if (playing != null) {
            _ = Stop();
            try {
                await playing.ConfigureAwait(false);
            }
            catch { }
        }
        /// <see cref="_cancellationTokenSource"/> will be disposed by the continuation
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) == 1)
            return;
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    [Serializable]
    public class ProcessingException : Exception
    {
        public IPlayerCommand? Command { get; init; }
        public Exception? OriginalException { get; init; }
        public ProcessingException() { }
        public ProcessingException(string? message) : base(message) { }
        public ProcessingException(string? message, Exception? innerException) : base(message, innerException) { }
        protected ProcessingException(SerializationInfo serializationInfo, StreamingContext streamingContext)
            : base(serializationInfo, streamingContext) { }
    }
}

public record struct PlayerStateChangedEventArgs(PlayerState PreviousState, PlayerState State);

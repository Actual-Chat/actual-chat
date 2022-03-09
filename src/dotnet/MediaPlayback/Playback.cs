using System.Threading.Tasks.Sources;
using ActualChat.Media;
using Microsoft.Extensions.Hosting;

namespace ActualChat.MediaPlayback;

/// <summary>
/// Represents an abstraction of array of <see cref="TrackPlayer"/>.
/// </summary>
public sealed class Playback : IAsyncDisposable
{
    private readonly ILogger<Playback> _log;
    private readonly CancellationToken _applicationStopping;
    private readonly ITrackPlayerFactory _trackPlayerFactory;
    private readonly Channel<CommandExecution> _commands;
    private int _isDisposed;
    private Task? _commandLoopTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private TaskSource<bool>? _whenCompleted;
    private readonly object _locker = new();
    private readonly object _runLocker = new();

    public readonly IMutableState<ImmutableList<(TrackInfo TrackInfo, PlayerState State)>> PlayingTracksState;
    /// <summary>
    /// Is <see langword="true" /> if <see cref="PlayTrackCommand"/> is enqueued or a track is actually being played.
    /// </summary>
    public readonly IMutableState<bool> IsPlayingState;

    public event Action<TrackInfo, PlayerState>? OnTrackPlayingChanged;

    internal Playback(IHostApplicationLifetime lifetime, IStateFactory stateFactory, ITrackPlayerFactory trackPlayerFactory, ILogger<Playback> log)
    {
        _commands = Channel.CreateBounded<CommandExecution>(
            new BoundedChannelOptions(128) {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
        PlayingTracksState = stateFactory.NewMutable(ImmutableList<(TrackInfo TrackInfo, PlayerState State)>.Empty);
        IsPlayingState = stateFactory.NewMutable(false);
        _trackPlayerFactory = trackPlayerFactory;
        _applicationStopping = lifetime.ApplicationStopping;
        _log = log;
    }

    private void RunCommandLoopIfNeeded()
    {
        if (_commandLoopTask != null)
            return;
        lock (_runLocker) {
            if (_commandLoopTask != null)
                return;

            if (_isDisposed == 1)
                throw new LifetimeException("Playback is disposed.", new ObjectDisposedException(nameof(TrackPlayer)));

            _whenCompleted = TaskSource.New<bool>(runContinuationsAsynchronously: true);
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_applicationStopping);
            _commandLoopTask = CommandLoop(_cancellationTokenSource.Token)
                .ContinueWith(task => {
                    lock (_runLocker) {
                        _cancellationTokenSource?.Dispose();
                        _cancellationTokenSource = null;
                        var exception = task.Exception?.InnerException ?? task.Exception;
                        if (exception != null)
                            _whenCompleted?.TrySetException(exception);
                        else
                            _whenCompleted?.TrySetResult(default);
                        _whenCompleted = null;
                        _commandLoopTask = null;
                        IsPlayingState.Value = false;
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    internal async Task CommandLoop(CancellationToken cancellationToken)
    {
        var trackPlayers = new ConcurrentDictionary<PlayTrackCommand, (TrackPlayer Player, Task RunningTask)>();
        try {
            var commands = _commands.Reader.ReadAllAsync(cancellationToken);
            await foreach (var execution in commands.ConfigureAwait(false).WithCancellation(cancellationToken)) {
                try {
                    switch (execution.Command) {
                        case PlayTrackCommand playTrackCommand:
                            await OnPlayTrackCommand(playTrackCommand, execution._whenAsyncOperationEnded).ConfigureAwait(false);
                            break;
                        case StopCommand:
                            await OnStopCommand().ConfigureAwait(false);
                            execution._whenAsyncOperationEnded.TrySetResult();
                            break;
                        default:
                            throw new NotSupportedException($"Unsupported command type: '{execution.Command.GetType()}'.");
                    }
                    // notify that the command processing is done
                    execution._whenCommandProcessed.TrySetResult();
                }
                catch (Exception ex) {
                    execution._whenCommandProcessed.TrySetException(ex);
                    execution._whenAsyncOperationEnded.TrySetException(ex);
                    throw;
                }
            }
        }
        finally {
            await WhenAllPlayers().ConfigureAwait(false);
        }

        Task OnStopCommand() => Task.WhenAll(trackPlayers.Values.Select(x => x.Player.Stop()));

        async ValueTask OnPlayTrackCommand(PlayTrackCommand command, TaskCompletionSource tcs)
        {
            if (trackPlayers.ContainsKey(command)) {
                // fail fast in case of wrong app logic, this shouldn't happen
                throw new InvalidOperationException($"{nameof(PlayTrackCommand)}:{command} added twice");
            }

            var trackPlayer = _trackPlayerFactory.Create(command.Source);
            var trackPlayerStateChanged =
                (PlayerStateChangedEventArgs args) => TrackPlayerStateChanged(command.TrackInfo, args);
            trackPlayer.StateChanged += trackPlayerStateChanged;
            var task = RunPlayer(trackPlayer, cancellationToken);
            if (!trackPlayers.TryAdd(command, (trackPlayer, task))) {
                await trackPlayer.DisposeAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"Can't add player for command {command}.");
            }

            async Task RunPlayer(TrackPlayer player, CancellationToken cancellationToken)
            {
                try {
                    await player.Play(cancellationToken).ConfigureAwait(false);
                    tcs.TrySetResult();
                }
                catch (Exception ex) {
                    tcs.TrySetException(ex);
                    throw;
                }
                finally {
                    player.StateChanged -= trackPlayerStateChanged;
                    trackPlayers.TryRemove(command, out var _);
                    await player.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        Task WhenAllPlayers() => Task.WhenAll(trackPlayers.Values.Select(x => x.RunningTask).ToArray());
    }

    private async ValueTask<CommandExecution> EnqueueCommand(IPlaybackCommand command, CancellationToken cancellationToken = default)
    {
        if (_isDisposed == 1)
            throw new LifetimeException("Playback is disposed.", new ObjectDisposedException(nameof(Playback)));
        RunCommandLoopIfNeeded();
        CommandExecution execution = new(command);
        await _commands.Writer.WriteAsync(execution, cancellationToken).ConfigureAwait(false);
        return execution;
    }

    /// <summary>
    /// Returns a <seealso cref="ValueTask{T}"/> which is completed when <seealso cref="PlayTrackCommand"/> is enqueued.
    /// </summary>
    public async ValueTask<CommandExecution> Play(
        TrackInfo trackInfo,
        IMediaSource source,
        Moment playAt, // By CpuClock
        CancellationToken cancellationToken = default)
    {
        // TODO: think about it, maybe requires a change ?
        IsPlayingState.Value = true;
        var command = new PlayTrackCommand(trackInfo, source) { PlayAt = playAt };
        return await EnqueueCommand(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a <seealso cref="ValueTask{T}"/> which is completed when <seealso cref="StopCommand"/> is enqueued.
    /// </summary>
    public ValueTask<CommandExecution> Stop(CancellationToken cancellationToken = default)
        => EnqueueCommand(StopCommand.Instance, cancellationToken);

    private async ValueTask DisposeAsyncCore()
    {
        var playing = Interlocked.Exchange(ref _commandLoopTask, null);
        if (playing != null) {
            _cancellationTokenSource?.Cancel();
            try {
                await playing.ConfigureAwait(false);
            }
            catch { }
            finally {
                _commands.Writer.TryComplete();
                OnTrackPlayingChanged = null;
            }
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

    private void TrackPlayerStateChanged(TrackInfo trackInfo, PlayerStateChangedEventArgs args)
    {
        var (prev, state) = args;
        try {
            OnTrackPlayingChanged?.Invoke(trackInfo, state);
        }
        catch (Exception ex) {
            _log.LogError(ex, $"Unhandled exception in {nameof(OnTrackPlayingChanged)}");
        }
        if (!prev.IsStarted && state.IsStarted) {
            lock (_locker) {
                PlayingTracksState.Value = PlayingTracksState.Value.Insert(0, (trackInfo, state));
                if (!IsPlayingState.Value) {
                    IsPlayingState.Value = true;
                }
            }
        }
        else if (state.IsCompleted && !prev.IsCompleted) {
            lock (_locker) {
                PlayingTracksState.Value = PlayingTracksState.Value.RemoveAll(x => x.TrackInfo.TrackId == trackInfo.TrackId);
                if (PlayingTracksState.Value.Count == 0) {
                    IsPlayingState.Value = false;
                }
            }
        }
    }

    public sealed record CommandExecution
    {
        public readonly IPlaybackCommand Command;
        internal readonly TaskCompletionSource _whenCommandProcessed;
        internal readonly TaskCompletionSource _whenAsyncOperationEnded;

        public CommandExecution(IPlaybackCommand command)
        {
            Command = command;
            /// TODO: use <see cref="TaskSource{T}"/> (?)
            _whenCommandProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _whenAsyncOperationEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Represents a task which will be completed when enqueued command is completed. <br/>
        /// For example for <seealso cref="PlayTrackCommand"/> when playing of a track is started (and after that you
        /// can get state changes from <see cref="PlayingTracksState"/> )
        /// </summary>
        public Task WhenCommandProcessed => _whenCommandProcessed.Task;

        /// <summary>
        /// Represents a task which will be completed when an async operation created by processed command is ended. <br/>
        /// For example for <seealso cref="PlayTrackCommand"/> when actual playing of a track is ended (on js side and
        /// after that you can enqueue the same track again)
        /// </summary>
        public Task WhenAsyncOperationEnded => _whenAsyncOperationEnded.Task;
    }
}

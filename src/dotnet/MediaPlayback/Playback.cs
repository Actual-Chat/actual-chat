using ActualChat.Media;

namespace ActualChat.MediaPlayback;

/// <summary>
/// Represents an abstraction of array of <see cref="TrackPlayer"/>.
/// </summary>
public sealed class Playback : IAsyncDisposable
{
    private readonly ILogger<Playback> _log;
    private readonly CancellationToken _applicationStopping;

    private readonly ITrackPlayerFactory _trackPlayerFactory;
    private readonly Channel<IPlaybackCommand> _commands;
    private int _isDisposed;
    private int _isRunning;
    private Task? _runningTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private TaskSource<bool>? _whenCompleted;
    private readonly object _locker = new();

    public readonly IMutableState<ImmutableList<(TrackInfo TrackInfo, PlayerState State)>> PlayingTracksState;
    public readonly IMutableState<bool> IsPlayingState;

    public event Action<TrackInfo, PlayerState>? OnTrackPlayingChanged;

    internal Playback(IStateFactory stateFactory, ITrackPlayerFactory trackPlayerFactory, ILogger<Playback> log)
    {
        _commands = Channel.CreateBounded<IPlaybackCommand>(
            new BoundedChannelOptions(128) {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
        PlayingTracksState = stateFactory.NewMutable(ImmutableList<(TrackInfo TrackInfo, PlayerState State)>.Empty);
        IsPlayingState = stateFactory.NewMutable(false);
        _trackPlayerFactory = trackPlayerFactory;
        _log = log;
    }

    public Task Run(CancellationToken cancellationToken = default)
    {
        if (_isDisposed == 1)
            throw new LifetimeException("Playback is disposed.", new ObjectDisposedException(nameof(TrackPlayer)));

        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) == 1)
            throw new LifetimeException("Playback is already running.");
        _whenCompleted = TaskSource.New<bool>(runContinuationsAsynchronously: true);
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_applicationStopping, cancellationToken);
        _runningTask = RunInternal(_cancellationTokenSource.Token)
                    .ContinueWith(task => {
                        _cancellationTokenSource?.Dispose();
                        _cancellationTokenSource = null;
                        var exception = task.Exception?.InnerException ?? task.Exception;
                        if (exception != null)
                            _whenCompleted?.TrySetException(exception);
                        else
                            _whenCompleted?.TrySetResult(default);
                        _whenCompleted = null;
                        _runningTask = null;
                        if (Interlocked.CompareExchange(ref _isRunning, 0, 1) == 0)
                            throw new LifetimeException("Trying to stop playback, while playing wasn't started.");
                        IsPlayingState.Value = false;
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return _runningTask;
    }

    internal async Task RunInternal(CancellationToken cancellationToken)
    {
        var trackPlayers = new ConcurrentDictionary<PlayTrackCommand, (TrackPlayer Player, Task RunningTask)>();
        try {
            var commands = _commands.Reader.ReadAllAsync(cancellationToken);
            await foreach (var command in commands.ConfigureAwait(false).WithCancellation(cancellationToken)) {
                switch (command) {
                    case PlayTrackCommand playTrackCommand:
                        await OnPlayTrackCommand(playTrackCommand).ConfigureAwait(false);
                        break;
                    case StopCommand:
                        OnStopCommand();
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported command type: '{command.GetType()}'.");
                }
            }
        }
        finally {
            await WhenAllPlayers().ConfigureAwait(false);
        }

        void OnStopCommand()
        {
            foreach (var (Player, _) in trackPlayers.Values) {
                Player.Stop();
            }
        }

        async ValueTask OnPlayTrackCommand(PlayTrackCommand command)
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
                await player.Play(cancellationToken).ConfigureAwait(false);
                player.StateChanged -= trackPlayerStateChanged;
                trackPlayers.TryRemove(command, out var _);
                await player.DisposeAsync().ConfigureAwait(false);
            }
        }

        Task WhenAllPlayers() => Task.WhenAll(trackPlayers.Values.Select(x => x.RunningTask).ToArray());
    }
    public void Stop() => _cancellationTokenSource?.Cancel();

    private ValueTask EnqueueCommand(IPlaybackCommand command, CancellationToken cancellationToken = default)
    {
        if (_isDisposed == 1)
            throw new LifetimeException("Playback is disposed.", new ObjectDisposedException(nameof(Playback)));
        if (_isRunning == 0)
            throw new LifetimeException($"Trying to enqueue command: {command} while playback isn't running.");
        return _commands.Writer.WriteAsync(command, cancellationToken);
    }

    public ValueTask Play(
        TrackInfo trackInfo,
        IMediaSource source,
        Moment playAt, // By CpuClock
        CancellationToken cancellationToken = default)
    {
        var command = new PlayTrackCommand(trackInfo, source) { PlayAt = playAt };
        return EnqueueCommand(command, cancellationToken);
    }

    public ValueTask StopPlaying(CancellationToken cancellationToken = default)
        => EnqueueCommand(StopCommand.Instance, cancellationToken);

    private async ValueTask DisposeAsyncCore()
    {
        var playing = Interlocked.Exchange(ref _runningTask, null);
        if (playing != null) {
            Stop();
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
            }
        }
        else if (state.IsCompleted && !prev.IsCompleted) {
            lock (_locker) {
                PlayingTracksState.Value = PlayingTracksState.Value.RemoveAll(x => x.TrackInfo.TrackId == trackInfo.TrackId);
            }
        }
    }
}

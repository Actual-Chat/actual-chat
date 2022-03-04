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
    private readonly Channel<IPlaybackCommand> _commands;
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

    private ValueTask EnqueueCommand(IPlaybackCommand command, CancellationToken cancellationToken = default)
    {
        if (_isDisposed == 1)
            throw new LifetimeException("Playback is disposed.", new ObjectDisposedException(nameof(Playback)));
        RunCommandLoopIfNeeded();
        return _commands.Writer.WriteAsync(command, cancellationToken);
    }

    public async ValueTask Play(
        TrackInfo trackInfo,
        IMediaSource source,
        Moment playAt, // By CpuClock
        CancellationToken cancellationToken = default)
    {
        var command = new PlayTrackCommand(trackInfo, source) { PlayAt = playAt };
        await EnqueueCommand(command, cancellationToken).ConfigureAwait(false);
        // we don't wait to actual playing for this flag (but if it's needed you can remove this line and it will wait)
        IsPlayingState.Value = true;
    }

    public ValueTask Stop(CancellationToken cancellationToken = default)
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
}

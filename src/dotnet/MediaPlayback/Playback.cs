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
    private readonly Channel<PlaybackCommandState> _commandStates;
    private Task? _commandLoopTask;
    private CancellationTokenSource? _commandLoopCts;
    private TaskSource<bool>? _whenCompleted;
    private readonly object _stateLocker = new();
    private readonly object _lock = new();
    private volatile bool _isDisposed;

    public readonly IMutableState<ImmutableList<(TrackInfo TrackInfo, PlayerState State)>> PlayingTracksState;
    public readonly IMutableState<bool> IsPlayingState;

    public event Action<TrackInfo, PlayerState>? OnTrackPlayingChanged;

    internal Playback(
        IStateFactory stateFactory,
        ITrackPlayerFactory trackPlayerFactory,
        IHostApplicationLifetime lifetime,
        ILogger<Playback> log)
    {
        _commandStates = Channel.CreateBounded<PlaybackCommandState>(
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

    public async ValueTask DisposeAsync()
    {
        lock (_lock) {
            if (_isDisposed)
                return;
            _isDisposed = true;
        }
        await DisposeAsyncCore().ConfigureAwait(false);
        // GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeAsyncCore()
    {
        var playing = Interlocked.Exchange(ref _commandLoopTask, null);
        if (playing != null) {
            // cancel the command loop enumeration
            // -> cancel + await running tasks of players
            // -> send StopCommand
            // -> await js callback
            _commandLoopCts?.Cancel();
            try {
                await playing.ConfigureAwait(false);
            }
            catch { }
            finally {
                _commandStates.Writer.TryComplete();
                OnTrackPlayingChanged = null;
            }
        }
        // <see cref="_commandLoopCts"/> will be disposed by the continuation
    }

    /// <summary>
    /// Returns a <seealso cref="ValueTask{T}"/> which is completed when <seealso cref="PlayTrackCommand"/> is enqueued.
    /// </summary>
    public async ValueTask<PlaybackCommandState> Play(
        TrackInfo trackInfo,
        IMediaSource source,
        Moment playAt, // By CpuClock
        CancellationToken cancellationToken)
    {
        // TODO: think about it, maybe it should be changed ?
        lock (_stateLocker)
            IsPlayingState.Value = true;
        var command = new PlayTrackCommand(trackInfo, source, cancellationToken) { PlayAt = playAt };
        return await EnqueueCommand(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a <seealso cref="ValueTask{T}"/> which is completed when <seealso cref="StopCommand"/> is enqueued.
    /// </summary>
    public ValueTask<PlaybackCommandState> Stop(CancellationToken cancellationToken)
        => EnqueueCommand(StopCommand.Instance, cancellationToken);

    private void StartCommandLoop()
    {
        if (_commandLoopTask != null)
            return;
        lock (_lock) {
            if (_commandLoopTask != null)
                return;
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(TrackPlayer));

            _whenCompleted = TaskSource.New<bool>(runContinuationsAsynchronously: true);
            _commandLoopCts = CancellationTokenSource.CreateLinkedTokenSource(_applicationStopping);
            _commandLoopTask = CommandLoop(_commandLoopCts.Token)
                .ContinueWith(task => {
                    lock (_lock) {
                        _commandLoopCts?.Dispose();
                        _commandLoopCts = null;
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

    private async Task CommandLoop(CancellationToken cancellationToken)
    {
        var trackPlayers = new ConcurrentDictionary<PlayTrackCommand, (TrackPlayer Player, Task RunningTask)>();
        try {
            var commandStates = _commandStates.Reader.ReadAllAsync(cancellationToken);
            await foreach (var commandState in commandStates.ConfigureAwait(false)) {
                try {
                    // if a Play call was cancelled we should skip all enqueued commands from this call
                    // note, that stop commands don't use the cancellation token at all
                    if (commandState.Command.CancellationToken.IsCancellationRequested)
                        continue;
                    switch (commandState.Command) {
                        case PlayTrackCommand playTrackCommand:
                            await OnPlayTrackCommand(playTrackCommand, commandState).ConfigureAwait(false);
                            break;
                        case StopCommand:
                            await OnStopCommand().ConfigureAwait(false);
                            commandState.MarkCompleted();
                            break;
                        default:
                            throw new NotSupportedException($"Unsupported command type: '{commandState.Command.GetType()}'.");
                    }
                    // notify that the command processing is done
                    commandState.MarkStarted();
                }
                catch (Exception ex) {
                    commandState.MarkCompleted(ex);
                    throw;
                }
            }
        }
        finally {
            await WhenAllPlayers().ConfigureAwait(false);
        }

        // There is no more method code from here - just local functions

        async Task OnStopCommand()
        {
            await Task.WhenAll(trackPlayers.Values.Select(x => x.Player.Stop())).ConfigureAwait(false);
            // after stop we should renew _playingCts, because user can send a next play command
        }

        async ValueTask OnPlayTrackCommand(PlayTrackCommand command, PlaybackCommandState commandState)
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

            async Task RunPlayer(TrackPlayer player, CancellationToken cancellationToken1)
            {
                try {
                    await player.Play(cancellationToken1).ConfigureAwait(false);
                    commandState.MarkCompleted();
                }
                catch (Exception ex) {
                    commandState.MarkCompleted(ex);
                    throw;
                }
                finally {
                    player.StateChanged -= trackPlayerStateChanged;
                    trackPlayers.TryRemove(command, out var _);
                    await player.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        Task WhenAllPlayers()
            => Task.WhenAll(trackPlayers.Values.Select(x => x.RunningTask).ToArray());
    }

    private async ValueTask<PlaybackCommandState> EnqueueCommand(IPlaybackCommand command, CancellationToken cancellationToken = default)
    {
        StartCommandLoop();
        PlaybackCommandState commandState = new(command);
        await _commandStates.Writer.WriteAsync(commandState, cancellationToken).ConfigureAwait(false);
        return commandState;
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
            lock (_stateLocker) {
                PlayingTracksState.Value = PlayingTracksState.Value.Insert(0, (trackInfo, state));
                IsPlayingState.Value = true;
            }
        }
        else if (state.IsCompleted && !prev.IsCompleted) {
            lock (_stateLocker) {
                PlayingTracksState.Value = PlayingTracksState.Value.RemoveAll(x => x.TrackInfo.TrackId == trackInfo.TrackId);
                if (PlayingTracksState.Value.Count == 0)
                    IsPlayingState.Value = false;
            }
        }
    }

}

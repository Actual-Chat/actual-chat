using ActualChat.Media;
using ActualChat.Messaging;

namespace ActualChat.MediaPlayback;

/// <summary>
/// Represents an abstraction of array of <see cref="TrackPlayer"/>.
/// </summary>
public sealed class Playback : ProcessorBase
{
    private readonly ILogger<Playback> _log;
    private readonly ITrackPlayerFactory _trackPlayerFactory;
    private readonly IMessageProcessor<IPlaybackCommand> _messageProcessor;
    private readonly ConcurrentDictionary<PlayTrackCommand, (TrackPlayer Player, Task PlayTask)> _trackPlayers = new();
    private readonly object _stateUpdateLock = new();

    public IMutableState<ImmutableList<(TrackInfo TrackInfo, PlayerState State)>> PlayingTracks { get; }
    public IMutableState<bool> IsPlaying { get; }
    public IMutableState<bool> IsPaused { get; }

    public event Action<TrackInfo, PlayerState>? OnTrackPlayingChanged;

    internal Playback(
        IStateFactory stateFactory,
        ITrackPlayerFactory trackPlayerFactory,
        ILogger<Playback> log)
    {
        _log = log;
        _trackPlayerFactory = trackPlayerFactory;
        _messageProcessor = new MessageProcessor<IPlaybackCommand>(ProcessCommand) {
            QueueFullMode = BoundedChannelFullMode.DropOldest,
        };
        PlayingTracks = stateFactory.NewMutable(ImmutableList<(TrackInfo TrackInfo, PlayerState State)>.Empty);
        IsPlaying = stateFactory.NewMutable(false);
        IsPaused = stateFactory.NewMutable(false);
    }

    protected override async Task DisposeAsyncCore()
    {
        var process = Stop(CancellationToken.None);
        await process.WhenCompleted.SuppressExceptions().ConfigureAwait(false);
        await _messageProcessor.Complete().SuppressExceptions().ConfigureAwait(false);
        await Task.WhenAll(_trackPlayers.Values.Select(x => x.PlayTask)).SuppressExceptions().ConfigureAwait(false);
        await _messageProcessor.DisposeAsync().ConfigureAwait(false);
    }

    public IMessageProcess<PlayTrackCommand> Play(
        TrackInfo trackInfo,
        IMediaSource source,
        Moment playAt, // By CpuClock
        CancellationToken cancellationToken)
    {
        lock (_stateUpdateLock)
            IsPlaying.Value = true;
        var command = new PlayTrackCommand(trackInfo, source) { PlayAt = playAt };
        return _messageProcessor.Enqueue(command, cancellationToken);
    }

    public IMessageProcess<PauseCommand> Pause(CancellationToken cancellationToken)
        => _messageProcessor.Enqueue(PauseCommand.Instance, cancellationToken);

    public IMessageProcess<ResumeCommand> Resume(CancellationToken cancellationToken)
        => _messageProcessor.Enqueue(ResumeCommand.Instance, cancellationToken);

    public IMessageProcess<StopCommand> Stop(CancellationToken cancellationToken)
        => _messageProcessor.Enqueue(StopCommand.Instance, cancellationToken);

    private Task<object?> ProcessCommand(IPlaybackCommand command, CancellationToken cancellationToken)
    {
        return command switch {
            PlayTrackCommand playTrackCommand => OnPlayTrackCommand(playTrackCommand),
            PauseCommand => OnPauseCommand(),
            ResumeCommand => OnResumeCommand(),
            StopCommand => OnStopCommand(),
            _ => throw new NotSupportedException($"Unsupported command type: '{command.GetType()}'.")
        };

        async Task<object?> OnPlayTrackCommand(PlayTrackCommand cmd)
        {
            if (_trackPlayers.ContainsKey(cmd))
                throw StandardError.StateTransition(GetType(),
                    $"The same {nameof(PlayTrackCommand)} is enqueued twice!");

            var trackPlayer = _trackPlayerFactory.Create(cmd.Source);
            // ReSharper disable once ConvertToLocalFunction
            var playerStateChanged = (PlayerStateChangedEventArgs args) => TrackPlayerStateChanged(cmd.TrackInfo, args);
            trackPlayer.StateChanged += playerStateChanged;

            var playTask = PlayTrack();
            if (!_trackPlayers.TryAdd(cmd, (trackPlayer, playTask))) {
                await trackPlayer.DisposeAsync().ConfigureAwait(false);
                throw StandardError.StateTransition(GetType(),
                    $"Can't register playback task; likely, the same {nameof(PlayTrackCommand)} is enqueued twice!");
            }

            return playTask;

            async Task<object?> PlayTrack()
            {
                try {
                    await trackPlayer.Play(cancellationToken).ConfigureAwait(false);
                    return null;
                }
                finally {
                    trackPlayer.StateChanged -= playerStateChanged;
                    _trackPlayers.TryRemove(cmd, out _);
                    await trackPlayer.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        async Task<object?> OnPauseCommand()
        {
            var pauseTasks = _trackPlayers.Values.Select(x => x.Player.Pause());
            await Task.WhenAll(pauseTasks).ConfigureAwait(false);
            return null;
        }

        async Task<object?> OnResumeCommand()
        {
            var resumeTasks = _trackPlayers.Values.Select(x => x.Player.Resume());
            await Task.WhenAll(resumeTasks).ConfigureAwait(false);
            return null;
        }

        async Task<object?> OnStopCommand()
        {
            var stopTasks = _trackPlayers.Values.Select(x => x.Player.Stop());
            await Task.WhenAll(stopTasks).ConfigureAwait(false);
            return null;
        }
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
            lock (_stateUpdateLock) {
                PlayingTracks.Value = PlayingTracks.Value.Insert(0, (trackInfo, state));
                IsPlaying.Value = true;
            }
        }
        else if (state.IsCompleted && !prev.IsCompleted) {
            lock (_stateUpdateLock) {
                PlayingTracks.Value = PlayingTracks.Value.RemoveAll(x => x.TrackInfo.TrackId == trackInfo.TrackId);
                if (PlayingTracks.Value.Count == 0) {
                    IsPlaying.Value = false;
                    IsPaused.Value = false;
                }
            }
        }
        if (prev.IsPaused ^ state.IsPaused) {
            lock (_stateUpdateLock) {
                IsPaused.Value = state.IsPaused;
            }
        }
    }
}

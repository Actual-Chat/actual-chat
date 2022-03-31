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

    public IMutableState<ImmutableList<(TrackInfo TrackInfo, PlayerState State)>> PlayingTracksState { get; }
    public IMutableState<bool> IsPlayingState { get; }

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
        PlayingTracksState = stateFactory.NewMutable(ImmutableList<(TrackInfo TrackInfo, PlayerState State)>.Empty);
        IsPlayingState = stateFactory.NewMutable(false);
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
            IsPlayingState.Value = true;
        var command = new PlayTrackCommand(trackInfo, source) { PlayAt = playAt };
        return _messageProcessor.Enqueue(command, cancellationToken);
    }

    public IMessageProcess<StopCommand> Stop(CancellationToken cancellationToken)
        => _messageProcessor.Enqueue(StopCommand.Instance, cancellationToken);

    private Task<object?> ProcessCommand(IPlaybackCommand command, CancellationToken cancellationToken)
    {
        return command switch {
            PlayTrackCommand playTrackCommand => OnPlayTrackCommand(playTrackCommand),
            StopCommand => OnStopCommand(),
            _ => throw new NotSupportedException($"Unsupported command type: '{command.GetType()}'.")
        };

        async Task<object?> OnPlayTrackCommand(PlayTrackCommand cmd)
        {
            if (_trackPlayers.ContainsKey(cmd))
                throw new LifetimeException($"The same {nameof(PlayTrackCommand)} is enqueued twice!");

            var trackPlayer = _trackPlayerFactory.Create(cmd.Source);
            // ReSharper disable once ConvertToLocalFunction
            var playerStateChanged = (PlayerStateChangedEventArgs args) => TrackPlayerStateChanged(cmd.TrackInfo, args);
            trackPlayer.StateChanged += playerStateChanged;

            var playTask = PlayTrack();
            if (!_trackPlayers.TryAdd(cmd, (trackPlayer, playTask))) {
                await trackPlayer.DisposeAsync().ConfigureAwait(false);
                throw new LifetimeException(
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

        async Task<object?> OnStopCommand()
        {
            await Task.WhenAll(_trackPlayers.Values.Select(x => x.Player.Stop())).ConfigureAwait(false);
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
                PlayingTracksState.Value = PlayingTracksState.Value.Insert(0, (trackInfo, state));
                IsPlayingState.Value = true;
            }
        }
        else if (state.IsCompleted && !prev.IsCompleted) {
            lock (_stateUpdateLock) {
                PlayingTracksState.Value = PlayingTracksState.Value.RemoveAll(x => x.TrackInfo.TrackId == trackInfo.TrackId);
                if (PlayingTracksState.Value.Count == 0)
                    IsPlayingState.Value = false;
            }
        }
    }
}

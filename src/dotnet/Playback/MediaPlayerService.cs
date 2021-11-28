using System.Collections.Concurrent;
using System.Diagnostics;
using Stl.Comparison;
using Stl.Concurrency;

namespace ActualChat.Playback;

public abstract class MediaPlayerService : IMediaPlayerService
{
    private readonly ConcurrentDictionary<Symbol, MediaTrackPlaybackState> _playbackStates = new ();

    protected IServiceProvider Services { get; }
    protected ILogger<MediaPlayerService> Log { get; }
    protected CancellationTokenSource StopTokenSource { get; }
    protected CancellationToken StopToken { get; }

    protected MediaPlayerService(
        IServiceProvider services,
        ILogger<MediaPlayerService> log)
    {
        Log = log;
        Services = services;
        StopTokenSource = new ();
        StopToken = StopTokenSource.Token;
    }

    public ValueTask DisposeAsync()
    {
        StopTokenSource.CancelAndDisposeSilently();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async Task Play(IAsyncEnumerable<MediaPlayerCommand> commands, CancellationToken cancellationToken)
    {
        using var linkedTokenSource = cancellationToken.LinkWith(StopToken);
        var linkedToken = linkedTokenSource.Token;

        var trackPlayers = new ConcurrentDictionary<Ref<PlayMediaTrackCommand>, MediaTrackPlayer>();
        await foreach (var command in commands.WithCancellation(linkedToken).ConfigureAwait(false))
            switch (command) {
            case PlayMediaTrackCommand playTrackCommand:
                var commandRef = Ref.New(playTrackCommand); // Reference-based comparison works faster for records
                if (trackPlayers.ContainsKey(commandRef))
                    continue;
                var trackPlayer = CreateMediaTrackPlayer(playTrackCommand);
                trackPlayers[commandRef] = trackPlayer;
                trackPlayer.StateChanged += OnStateChanged;
                _ = trackPlayer.Run(linkedToken).ContinueWith(
                    _ => {
                        trackPlayers.TryRemove(commandRef, out var _);
                    },
                    TaskScheduler.Default);
                break;
            case SetVolumeCommand setVolume:
                var volumeTasks = trackPlayers.Values
                    .Select(p => p.EnqueueCommand(new SetTrackVolumeCommand(p, setVolume.Volume)).AsTask())
                    .ToArray();
                await Task.WhenAll(volumeTasks).ConfigureAwait(false);
                break;
            case StopCommand stopCommand:
                try {
                    var stopTasks = trackPlayers.Values
                        .Select(p => p.EnqueueCommand(new StopPlaybackCommand(p, true)).AsTask())
                        .ToArray();
                    await Task.WhenAll(stopTasks).ConfigureAwait(false);
                }
                finally {
                    stopCommand.CommandProcessedSource.SetResult();
                }
                break;
            default:
                throw new NotSupportedException($"Unsupported command type: '{command.GetType()}'.");
            }

        // TODO(AK): to make this code reachable, the command sequence must have an end
        // ~= await Task.WhenAll(unfinishedPlayTasks).ConfigureAwait(false);
        while (true) {
            var (commandRef, trackPlayer) = trackPlayers.FirstOrDefault();
            if (trackPlayer == null!)
                break;

            await (trackPlayer.RunningTask ?? Task.CompletedTask).ConfigureAwait(false);
            trackPlayers.TryRemove(commandRef, trackPlayer);
        }
    }

    public virtual Task<MediaTrackPlaybackState?> GetMediaTrackPlaybackState(
        Symbol trackId,
        CancellationToken cancellationToken)
        => Task.FromResult(_playbackStates.GetValueOrDefault(trackId));

    public virtual Task<MediaTrackPlaybackState?> GetMediaTrackPlaybackState(
        Symbol trackId,
        Range<Moment> timestampRange,
        CancellationToken cancellationToken)
    {
        _ = PlaybackConstants.TimeTileStack.GetTile(timestampRange);
        var state = _playbackStates.GetValueOrDefault(trackId);
        if (state == null)
            return Task.FromResult(state);

        var timestamp = state.RecordingStartedAt + state.PlayingAt;
        var result = timestampRange.Contains(timestamp) ? state : null;
        return Task.FromResult(result);
    }

    public void RegisterDefaultMediaTrackState(MediaTrackPlaybackState state)
        => _playbackStates[state.TrackId] = state;

    // Protected methods

    protected abstract MediaTrackPlayer CreateMediaTrackPlayer(PlayMediaTrackCommand mediaTrack);

    protected void OnStateChanged(MediaTrackPlaybackState lastState, MediaTrackPlaybackState state)
    {
        // Debug.WriteLine($"StateChanged: " +
        //     $"({lastState.PlayingAt}, {lastState.IsStarted}, {lastState.IsCompleted}) ->" +
        //     $"({state.PlayingAt}, {state.IsStarted}, {state.IsCompleted})");
        var trackId = state.TrackId;
        var timeTileStack = PlaybackConstants.TimeTileStack;
        if (state.IsCompleted)
            _playbackStates.TryRemove(trackId, out _);
        else
            _playbackStates[trackId] = state;

        using (Computed.Invalidate()) {
            _ = GetMediaTrackPlaybackState(trackId, default);

            // Invalidating GetPlayingMediaFrame for tiles associated with lastState.PlayingAt
            var lastTimestamp = lastState.RecordingStartedAt + lastState.PlayingAt;
            foreach (var tile in timeTileStack.GetAllTiles(lastTimestamp))
                _ = GetMediaTrackPlaybackState(trackId, tile.Range, default);

            // Invalidating GetPlayingMediaFrame for tiles associated with state.PlayingAt
            var timestamp = state.RecordingStartedAt + state.PlayingAt;
            foreach (var tile in timeTileStack.GetAllTiles(timestamp))
                _ = GetMediaTrackPlaybackState(trackId, tile.Range, default);
        }
    }
}

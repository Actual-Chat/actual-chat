using System.Collections.Concurrent;
using ActualChat.Mathematics;
using Stl.Comparison;
using Stl.Concurrency;

namespace ActualChat.Playback;

public abstract class MediaPlayerService : AsyncDisposableBase, IMediaPlayerService
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
                    trackPlayer.StateChanged += OnStateChanged;
                    _ = trackPlayer.Run(linkedToken);
                    _ = trackPlayer.RunningTask!.ContinueWith(
                        // We want to remove players once they finish, otherwise it may cause mem leak
                        _ => {
                            _playbackStates.TryRemove(trackPlayer.State.TrackId, trackPlayer.State);
                            return trackPlayers.TryRemove(commandRef, trackPlayer);
                        },
                        TaskScheduler.Default);
                    trackPlayers[commandRef] = trackPlayer;
                    break;
                case SetVolumeCommand setVolume:
                    var tasks = trackPlayers.Values
                        .Select(p => p.EnqueueCommand(new SetTrackVolumeCommand(p, setVolume.Volume)).AsTask())
                        .ToArray();
                    await Task.WhenAll(tasks).ConfigureAwait(false);
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

    public virtual Task<bool> IsPlaybackCompleted(
        Symbol trackId,
        CancellationToken cancellationToken)
    {
        var state = _playbackStates.GetValueOrDefault(trackId);
        return Task.FromResult(state == null || state.IsCompleted);
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
        PlaybackConstants.TimestampTiles.AssertIsTile(timestampRange);
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
        var trackId = state.TrackId;
        var timestampLogCover = PlaybackConstants.TimestampTiles;
        if (state.IsCompleted)
            _playbackStates.TryRemove(trackId, out _);
        else
            _playbackStates[trackId] = state;

        using (Computed.Invalidate()) {
            if (state.IsCompleted != lastState.IsCompleted)
                _ = IsPlaybackCompleted(trackId, default);

            _ = GetMediaTrackPlaybackState(trackId, default);

            // Invalidating GetPlayingMediaFrame for tiles associated with lastState.PlayingAt
            var lastTimestamp = lastState.RecordingStartedAt + lastState.PlayingAt;
            foreach (var tile in timestampLogCover.GetCoveringTiles(lastTimestamp))
                _ = GetMediaTrackPlaybackState(trackId, tile, default);

            // Invalidating GetPlayingMediaFrame for tiles associated with state.PlayingAt
            var timestamp = state.RecordingStartedAt + state.PlayingAt;
            foreach (var tile in timestampLogCover.GetCoveringTiles(timestamp))
                _ = GetMediaTrackPlaybackState(trackId, tile, default);
        }
    }

    protected override ValueTask DisposeAsyncCore()
    {
        Stop();
        return ValueTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;

        Stop();
    }

    protected void Stop()
    {
        if (StopTokenSource.IsCancellationRequested)
            return;

        try {
            StopTokenSource.Cancel();
        }
        catch {
            // Intended
        }
    }
}

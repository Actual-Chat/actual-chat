using System.Collections.Concurrent;
using ActualChat.Mathematics;
using Stl.Comparison;
using Stl.Concurrency;

namespace ActualChat.Playback;

public abstract class MediaPlayerService : AsyncDisposableBase, IMediaPlayerService
{
    private readonly ConcurrentDictionary<Symbol, TrackPlaybackState> _playbackStateStorage = new ();

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
        StopTokenSource = new CancellationTokenSource();
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
                    trackPlayer.PlaybackStateChanged += OnPlaybackStateChanged;
                    _ = trackPlayer.Run(linkedToken);
                    _ = trackPlayer.RunningTask!.ContinueWith(
                        // We want to remove players once they finish, otherwise it may cause mem leak
                        _ => trackPlayers.TryRemove(commandRef, trackPlayer),
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

    public virtual Task<TrackPlaybackState?> GetPlayingMediaFrame(
        Symbol trackId,
        CancellationToken cancellationToken)
        => Task.FromResult(_playbackStateStorage.GetValueOrDefault(trackId));

    public virtual Task<TrackPlaybackState?> GetPlayingMediaFrame(
        Symbol trackId,
        Range<Moment> timestampRange,
        CancellationToken cancellationToken)
    {
        PlaybackConstants.TimestampLogCover.AssertIsTile(timestampRange);
        var state = _playbackStateStorage.GetValueOrDefault(trackId);
        if (state == null)
            return Task.FromResult(state);

        var timestamp = state.RecordingStartedAt + state.PlayingAt;
        var result = timestampRange.Contains(timestamp) ? state : null;
        return Task.FromResult(result);
    }

    // Protected methods

    protected abstract MediaTrackPlayer CreateMediaTrackPlayer(PlayMediaTrackCommand mediaTrack);

    protected void OnPlaybackStateChanged(TrackPlaybackState state)
    {
        var trackId = state.TrackId;
        var timestampLogCover = PlaybackConstants.TimestampLogCover;
        if (state.Completed)
            _playbackStateStorage.TryRemove(trackId, out _);
        else
            _playbackStateStorage[trackId] = state;
        using (Computed.Invalidate()) {
            _ = GetPlayingMediaFrame(trackId, default);

            var timestamp = state.RecordingStartedAt + state.PlayingAt;
            foreach (var tile in timestampLogCover.GetCoveringTiles(timestamp)) {
                // TODO(AK): this line throws exceptions - invalid range boundaries!
                // _ = GetPlayingMediaFrame(trackId, tile, default);
            }
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

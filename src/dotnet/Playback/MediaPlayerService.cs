using System.Collections.Concurrent;
using ActualChat.Mathematics;
using Stl.Comparison;
using Stl.Concurrency;

namespace ActualChat.Playback;

public abstract class MediaPlayerService : AsyncDisposableBase, IMediaPlayerService
{
    private readonly ConcurrentDictionary<Symbol, PlayMediaFrameCommand> _playingFrames = new ();

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
                    trackPlayer.CommandProcessed += OnTrackPlayerCommandCommandProcessed;
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

        //TODO(AK): this code will not be reached!
        // ~= await Task.WhenAll(unfinishedPlayTasks).ConfigureAwait(false);
        while (true) {
            var (commandRef, trackPlayer) = trackPlayers.FirstOrDefault();
            if (trackPlayer == null!)
                break;

            await (trackPlayer.RunningTask ?? Task.CompletedTask).ConfigureAwait(false);
            trackPlayers.TryRemove(commandRef, trackPlayer);
        }
    }

    public virtual Task<PlayMediaFrameCommand?> GetPlayingMediaFrame(
        Symbol trackId,
        CancellationToken cancellationToken)
        => Task.FromResult(_playingFrames.GetValueOrDefault(trackId));

    public virtual Task<PlayMediaFrameCommand?> GetPlayingMediaFrame(
        Symbol trackId,
        Range<Moment> timestampRange,
        CancellationToken cancellationToken)
    {
        PlaybackConstants.TimestampLogCover.AssertIsTile(timestampRange);
        var command = _playingFrames.GetValueOrDefault(trackId);
        if (command == null)
            return Task.FromResult(command);

        var timestamp = command.Player.Command.RecordingStartedAt + command.Frame.Offset;
        var result = timestampRange.Contains(timestamp) ? command : null;
        return Task.FromResult(result);
    }

    // Protected methods

    protected abstract MediaTrackPlayer CreateMediaTrackPlayer(PlayMediaTrackCommand mediaTrack);

    protected virtual ValueTask OnTrackPlayerCommandCommandProcessed(MediaTrackPlayerCommand command)
    {
        PlayMediaFrameCommand? frameCmd, prevFrameCmd;
        MediaTrackPlayer trackPlayer;
        switch (command) {
            case PlayMediaFrameCommand playCommand:
                trackPlayer = playCommand.Player;
                frameCmd = playCommand;
                prevFrameCmd = _playingFrames.GetValueOrDefault(playCommand.Player.Command.TrackId);
                break;
            case StopPlaybackCommand stopCommand:
                trackPlayer = stopCommand.Player;
                frameCmd = null;
                prevFrameCmd = _playingFrames.GetValueOrDefault(stopCommand.Player.Command.TrackId);
                break;
            default:
                return ValueTask.CompletedTask;
        }

        var trackId = trackPlayer.Command.TrackId;
        var timestampLogCover = PlaybackConstants.TimestampLogCover;
        if (frameCmd != null)
            _playingFrames[trackId] = frameCmd;
        else
            _playingFrames.TryRemove(trackId, out var _);
        using (Computed.Invalidate()) {
            _ = GetPlayingMediaFrame(trackId, default);
            if (prevFrameCmd != null) {
                var timestamp = trackPlayer.Command.RecordingStartedAt + prevFrameCmd.Frame.Offset;
                foreach (var tile in timestampLogCover.GetCoveringTiles(timestamp)) {
                    // TODO(AK): this line throws exceptions - invalid range boundaries!
                    // _ = GetPlayingMediaFrame(mediaTrack.Id, tile, default);
                }
            }
            if (frameCmd != null) {
                var timestamp = trackPlayer.Command.RecordingStartedAt + frameCmd.Frame.Offset;
                foreach (var tile in timestampLogCover.GetCoveringTiles(timestamp)) {
                    // TODO(AK): this line throws exceptions - invalid range boundaries!
                    // _ = GetPlayingMediaFrame(mediaTrack.Id, tile, default);
                }
            }
        }
        return ValueTask.CompletedTask;
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

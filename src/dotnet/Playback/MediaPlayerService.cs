using System.Collections.Concurrent;
using ActualChat.Mathematics;
using Stl.Concurrency;

namespace ActualChat.Playback;

public abstract class MediaPlayerService : AsyncDisposableBase, IMediaPlayerService
{
    private readonly ConcurrentDictionary<Symbol, PlayingMediaFrame> _playingFrames = new();

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

    /// <inheritdoc/>
    public async Task Play(IAsyncEnumerable<MediaTrack> tracks, CancellationToken cancellationToken)
    {
        using var linkedTokenSource = cancellationToken.LinkWith(StopToken);
        var linkedToken = linkedTokenSource.Token;

        var trackPlayers = new ConcurrentDictionary<MediaTrack, MediaTrackPlayer>();
        await foreach (var track in tracks.WithCancellation(linkedToken).ConfigureAwait(false)) {
            if (trackPlayers.ContainsKey(track))
                continue;
            var trackPlayer = CreateMediaTrackPlayer(track);
            trackPlayer.Playing += (prevFrame, nextFrame) => OnPlayingFrame(track, prevFrame, nextFrame);
            _ = trackPlayer.Run(linkedToken);
            _ = trackPlayer.RunningTask!.ContinueWith(
                _ => trackPlayers.TryRemove(track, trackPlayer),
                TaskScheduler.Default);
            trackPlayers[track] = trackPlayer;
        }

        // ~= await Task.WhenAll(unfinishedPlayTasks).ConfigureAwait(false);
        while (true) {
            var (track, trackPlayer) = trackPlayers.FirstOrDefault();
            if (track == null!)
                break;
            await (trackPlayer.RunningTask ?? Task.CompletedTask).ConfigureAwait(false);
            trackPlayers.TryRemove(track, trackPlayer);
        }
    }

    public virtual Task<PlayingMediaFrame?> GetPlayingMediaFrame(
        Symbol trackId,
        CancellationToken cancellationToken)
        => Task.FromResult(_playingFrames.GetValueOrDefault(trackId));

    public virtual Task<PlayingMediaFrame?> GetPlayingMediaFrame(
        Symbol trackId, Range<Moment> timestampRange,
        CancellationToken cancellationToken)
    {
        MediaPlayConstants.TimestampLogCover.AssertIsTile(timestampRange);
        var frame = _playingFrames.GetValueOrDefault(trackId);
        var result = frame != null && timestampRange.Contains(frame.Timestamp) ? frame : null;
        return Task.FromResult(result);
    }

    // Protected methods

    protected abstract MediaTrackPlayer CreateMediaTrackPlayer(MediaTrack mediaTrack);

    protected virtual void OnPlayingFrame(MediaTrack mediaTrack, PlayingMediaFrame? prevFrame, PlayingMediaFrame? nextFrame)
    {
        var timestampLogCover = MediaPlayConstants.TimestampLogCover;
        if (nextFrame != null)
            _playingFrames[mediaTrack.Id] = nextFrame;
        else
            _playingFrames.TryRemove(mediaTrack.Id, out var _);
        using (Computed.Invalidate()) {
            _ = GetPlayingMediaFrame(mediaTrack.Id, default);
            if (prevFrame != null) {
                foreach (var tile in timestampLogCover.GetCoveringTiles(prevFrame.Timestamp)) {
                    // TODO(AK): this line throws exceptions - invalid range boundaries!
                    // _ = GetPlayingMediaFrame(mediaTrack.Id, tile, default);
                }
            }
            if (nextFrame != null) {
                foreach (var tile in timestampLogCover.GetCoveringTiles(nextFrame.Timestamp)) {
                    // TODO(AK): this line throws exceptions - invalid range boundaries!
                    // _ = GetPlayingMediaFrame(mediaTrack.Id, tile, default);
                }
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

using System.Collections.Concurrent;
using ActualChat.Mathematics;

namespace ActualChat.Playback;

public class MediaPlayer : AsyncDisposableBase, IMediaPlayer
{
    private readonly ILogger<MediaPlayer> _logger;
    private readonly ConcurrentDictionary<Symbol, PlayingMediaFrame> _playingFrames = new();
    private readonly List<MediaTrackPlayer> _mediaTrackPlayers = new();

    protected IMediaTrackPlayerFactory MediaTrackPlayerFactory { get; init; }

    public MediaPlayer(IMediaTrackPlayerFactory mediaTrackPlayerFactory, ILogger<MediaPlayer> logger)
    {
        _logger = logger;
        MediaTrackPlayerFactory = mediaTrackPlayerFactory;
    }

    public async Task Play(IAsyncEnumerable<MediaTrack> tracks, CancellationToken cancellationToken)
    {
        await foreach (var playbackStream in tracks.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var trackPlayer = MediaTrackPlayerFactory.CreatePlayer(playbackStream);
            _mediaTrackPlayers.Add(trackPlayer);

            trackPlayer.Playing +=
                (prevFrame, nextFrame) => OnPlayingFrame(playbackStream, prevFrame, nextFrame);
            _ = trackPlayer.Play(cancellationToken).ContinueWith(ObserveException, TaskScheduler.Default);
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
        PlaybackConstants.TimestampLogCover.AssertIsTile(timestampRange);
        var frame = _playingFrames.GetValueOrDefault(trackId);
        var result = frame != null && timestampRange.Contains(frame.Timestamp) ? frame : null;
        return Task.FromResult(result);
    }

    // Protected methods

    protected virtual void OnPlayingFrame(MediaTrack mediaTrack, PlayingMediaFrame? prevFrame, PlayingMediaFrame? nextFrame)
    {
        var timestampLogCover = PlaybackConstants.TimestampLogCover;
        if (nextFrame != null)
            _playingFrames[mediaTrack.Id] = nextFrame;
        else
            _playingFrames.TryRemove(mediaTrack.Id, out var _);
        using (Computed.Invalidate()) {
            _ = GetPlayingMediaFrame(mediaTrack.Id, default);
            if (prevFrame != null) {
                foreach (var tile in timestampLogCover.GetCoveringTiles(prevFrame.Timestamp))
                    _ = GetPlayingMediaFrame(mediaTrack.Id, tile, default);
            }
            if (nextFrame != null) {
                foreach (var tile in timestampLogCover.GetCoveringTiles(nextFrame.Timestamp))
                    _ = GetPlayingMediaFrame(mediaTrack.Id, tile, default);
            }
        }
    }

    protected override async ValueTask DisposeInternal(bool disposing)
    {
        foreach (var mediaTrackPlayer in _mediaTrackPlayers)
            try {
                await mediaTrackPlayer.DisposeAsync();
            }
            catch (Exception e) {
                _logger.LogWarning(e, "error disposing media track player");
            }
    }

    private async Task ObserveException(Task playTask)
    {
        try {
            await playTask.ConfigureAwait(false);
        }
        catch (Exception e) when (e is not TaskCanceledException) {
            _logger.LogError(e, "Unable to play media track");
        }
    }
}

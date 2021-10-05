using System.Collections.Concurrent;
using ActualChat.Mathematics;

namespace ActualChat.Playback;

public class MediaPlayer : AsyncDisposableBase, IMediaPlayer
{
    private readonly ConcurrentDictionary<Symbol, PlayingMediaFrame> _playingFrames = new();

    protected Func<MediaTrack, MediaTrackPlayer> MediaTrackPlayerFactory { get; init; }

    public MediaPlayer(Func<MediaTrack, MediaTrackPlayer> mediaTrackPlayerFactory)
        => MediaTrackPlayerFactory = mediaTrackPlayerFactory;

    public async Task Play(IAsyncEnumerable<MediaTrack> tracks, CancellationToken cancellationToken)
    {
        await foreach (var playbackStream in tracks.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var trackPlayer = MediaTrackPlayerFactory.Invoke(playbackStream);
            trackPlayer.Playing +=
                (prevFrame, nextFrame) => OnPlayingFrame(playbackStream, prevFrame, nextFrame);
            _ = trackPlayer.Play(cancellationToken);
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
}

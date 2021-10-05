using System.Collections.Concurrent;
using ActualChat.Mathematics;
using ActualChat.Media;
using ActualChat.Playback.Internal;

namespace ActualChat.Playback;

public abstract class MediaPlayerBase : AsyncDisposableBase, IMediaPlayer
{
    public readonly ConcurrentDictionary<Symbol, MediaFrame> PlayingFrames = new();

    public async Task Play(IAsyncEnumerable<MediaTrack> tracks, CancellationToken cancellationToken)
    {
        await foreach (var playbackStream in tracks.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var channelPlayer = CreateTrackPlayer(playbackStream);
            channelPlayer.Playing +=
                (prevFrame, nextFrame) => OnPlayingFrame(playbackStream, prevFrame, nextFrame);
            _ = channelPlayer.Play(cancellationToken);
        }
    }

    public virtual Task<MediaFrame?> GetPlayingMediaFrame(
        Symbol trackId,
        CancellationToken cancellationToken)
        => Task.FromResult(PlayingFrames.GetValueOrDefault(trackId));

    public virtual Task<MediaFrame?> GetPlayingMediaFrame(
        Symbol trackId, Range<Moment> timestampRange,
        CancellationToken cancellationToken)
    {
        PlaybackConstants.TimestampLogCover.AssertIsTile(timestampRange);
        var frame = PlayingFrames.GetValueOrDefault(trackId);
        var result = frame != null && timestampRange.Contains(frame.Timestamp) ? frame : null;
        return Task.FromResult(result);
    }

    // Protected methods

    protected abstract MediaTrackPlayer CreateTrackPlayer(MediaTrack mediaTrack);

    protected virtual void OnPlayingFrame(MediaTrack mediaTrack, MediaFrame? prevFrame, MediaFrame? nextFrame)
    {
        var timestampLogCover = PlaybackConstants.TimestampLogCover;
        if (nextFrame != null)
            PlayingFrames[mediaTrack.Id] = nextFrame;
        else
            PlayingFrames.TryRemove(mediaTrack.Id, out var _);
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

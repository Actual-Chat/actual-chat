using System.Collections.Concurrent;
using ActualChat.Mathematics;
using ActualChat.Media;
using ActualChat.Playback.Internal;

namespace ActualChat.Playback;

public abstract class MediaPlayerBase<TMediaChannel, TMediaFormat, TMediaFrame>
    : AsyncDisposableBase, IMediaPlayer<TMediaChannel, TMediaFormat, TMediaFrame>
    where TMediaFormat : notnull
    where TMediaChannel : MediaChannel<TMediaFormat, TMediaFrame>
    where TMediaFrame : MediaFrame
{
    public readonly ConcurrentDictionary<(Symbol PlayId, Symbol ChannelId), TMediaFrame> _playingFrames = new();

    public async Task Play(Symbol playId, ChannelReader<TMediaChannel> source, CancellationToken cancellationToken)
    {
        while (await source.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        while (source.TryRead(out var mediaChannel)) {
            var channelPlayer = CreateChannelPlayer(mediaChannel);
            channelPlayer.Playing +=
                (prevFrame, nextFrame) => OnPlayingFrame(playId, mediaChannel, prevFrame, nextFrame);
            _ = channelPlayer.Play(cancellationToken);
        }
    }

    public virtual Task<TMediaFrame?> GetPlayingMediaFrame(
        Symbol playId, Symbol channelId,
        CancellationToken cancellationToken)
        => Task.FromResult(_playingFrames.GetValueOrDefault((playId, channelId)));

    public virtual Task<TMediaFrame?> GetPlayingMediaFrame(
        Symbol playId, Symbol channelId, Range<Moment> timestampRange,
        CancellationToken cancellationToken)
    {
        PlaybackConstants.TimestampLogCover.AssertIsTile(timestampRange);
        var mediaFrame = _playingFrames.GetValueOrDefault((playId, channelId));
        var result = mediaFrame != null && timestampRange.Contains(mediaFrame.Timestamp) ? mediaFrame : null;
        return Task.FromResult(result);
    }

    // Protected methods

    protected abstract MediaChannelPlayer<TMediaChannel, TMediaFormat, TMediaFrame> CreateChannelPlayer(TMediaChannel mediaChannel);

    protected virtual void OnPlayingFrame(Symbol playId, TMediaChannel mediaChannel, TMediaFrame? prevFrame, TMediaFrame? nextFrame)
    {
        var timestampLogCover = PlaybackConstants.TimestampLogCover;
        var channelId = mediaChannel.Id;
        var playAndChannelId = (playId, channelId);
        if (nextFrame != null)
            _playingFrames[playAndChannelId] = nextFrame;
        else
            _playingFrames.TryRemove(playAndChannelId, out var _);
        using (Computed.Invalidate()) {
            _ = GetPlayingMediaFrame(playId, channelId, default);
            if (prevFrame != null) {
                foreach (var tile in timestampLogCover.GetCoveringTiles(prevFrame.Timestamp))
                    _ = GetPlayingMediaFrame(playId, channelId, tile, default);
            }
            if (nextFrame != null) {
                foreach (var tile in timestampLogCover.GetCoveringTiles(nextFrame.Timestamp))
                    _ = GetPlayingMediaFrame(playId, channelId, tile, default);
            }
        }
    }
}

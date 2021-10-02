using ActualChat.Media;

namespace ActualChat.Playback;

public interface IMediaPlayer<TMediaChannel, TMediaFormat, TMediaFrame> : IAsyncDisposable
    where TMediaFormat : notnull
    where TMediaChannel : MediaChannel<TMediaFormat, TMediaFrame>
    where TMediaFrame : MediaFrame
{
    Task Play(Symbol playId, ChannelReader<TMediaChannel> source, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<TMediaFrame?> GetPlayingMediaFrame(
        Symbol playId, Symbol channelId,
        CancellationToken cancellationToken);
    [ComputeMethod]
    Task<TMediaFrame?> GetPlayingMediaFrame(
        Symbol playId, Symbol channelId, Range<Moment> timestampRange,
        CancellationToken cancellationToken);
}

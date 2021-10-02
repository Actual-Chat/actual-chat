using ActualChat.Media;

namespace ActualChat.Playback;

public interface IMediaPlayer<TMediaChannel, TMediaFormat, TMediaFrame>
    where TMediaFormat : notnull
    where TMediaChannel : MediaChannel<TMediaFormat, TMediaFrame>
    where TMediaFrame : MediaFrame
{
    Task Play(ChannelReader<TMediaChannel> channels, CancellationToken cancellationToken);
}

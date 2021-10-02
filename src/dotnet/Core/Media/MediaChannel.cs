namespace ActualChat.Media;

public abstract class MediaChannel<TMediaFormat, TMediaFrame>
    where TMediaFormat : notnull
    where TMediaFrame : MediaFrame
{
    public TMediaFormat Format { get; init; } = default!;
    public ChannelReader<TMediaFrame> Frames { get; init; } = null!;
}

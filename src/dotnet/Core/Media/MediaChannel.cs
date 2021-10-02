namespace ActualChat.Media;

public abstract class MediaChannel<TMediaFormat, TMediaFrame> : IHasId<Symbol>
    where TMediaFormat : notnull
    where TMediaFrame : MediaFrame
{
    public Symbol Id { get; init; } = Symbol.Empty;
    public TMediaFormat Format { get; init; } = default!;
    public ChannelReader<TMediaFrame> Frames { get; init; } = null!;
}

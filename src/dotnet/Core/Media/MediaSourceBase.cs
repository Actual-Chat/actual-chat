namespace ActualChat.Media;

public abstract class MediaSourceBase<TFormat, TFrame> : IMediaSource
    where TFormat : MediaFormat
    where TFrame : MediaFrame
{
    public MediaType Type => Format.Type;
    public TFormat Format { get; init; } = default!;
    public IAsyncEnumerable<TFrame> Frames { get; init; } = null!;

    MediaFormat IMediaSource.Format => Format;
    IAsyncEnumerable<MediaFrame> IMediaSource.Frames => Frames;
}

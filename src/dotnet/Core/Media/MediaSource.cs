namespace ActualChat.Media;

public interface IMediaSource
{
    MediaFormat Format { get; }
    IAsyncEnumerable<MediaFrame> Frames { get; }
}

public abstract class MediaSource<TFormat, TFrame> : IMediaSource, IAsyncEnumerable<TFrame>
    where TFormat : MediaFormat
    where TFrame : MediaFrame
{
    MediaFormat IMediaSource.Format => Format;
    public TFormat Format { get; }

    IAsyncEnumerable<MediaFrame> IMediaSource.Frames => Frames;
    public IAsyncEnumerable<TFrame> Frames { get; }

    protected MediaSource(TFormat format, IAsyncEnumerable<TFrame> frames)
    {
        Format = format;
        Frames = frames;
    }

    public IAsyncEnumerator<TFrame> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => Frames.GetAsyncEnumerator(cancellationToken);
}

namespace ActualChat.Media;

public interface IMediaSource
{
    MediaType Type { get; }
    MediaFormat Format { get; }
    IAsyncEnumerable<MediaFrame> Frames { get; }
}

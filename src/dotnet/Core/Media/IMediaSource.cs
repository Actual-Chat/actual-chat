namespace ActualChat.Media;

public interface IMediaSource
{
    bool IsCancelled { get; }
    MediaFormat Format { get; }
    TimeSpan Duration { get; }
    Task WhenFormatAvailable { get; }
    Task WhenDurationAvailable { get; }

    IAsyncEnumerable<MediaFrame> GetFramesUntyped(CancellationToken cancellationToken);
}

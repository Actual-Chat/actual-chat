namespace ActualChat.Media;

/// <summary>
/// Provides access to a stream of media frames with format metadata.
/// </summary>
public interface IMediaSource : IDisposable
{
    bool IsCancelled { get; }
    MediaFormat Format { get; }
    TimeSpan Duration { get; }
    Task WhenDurationAvailable { get; }

    IAsyncEnumerable<MediaFrame> GetFramesUntyped(CancellationToken cancellationToken);
}

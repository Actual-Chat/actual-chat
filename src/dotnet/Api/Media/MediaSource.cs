namespace ActualChat.Media;

/// <summary>
/// Base class providing a memoized stream of media frames with format metadata.
/// </summary>
public abstract class MediaSource<TFormat, TFrame> : IMediaSource
    where TFormat : MediaFormat
    where TFrame : MediaFrame
{
    protected AsyncMemoizer<TFrame> MemoizedFrames { get; }
    protected AsyncTaskMethodBuilder<TimeSpan> DurationTaskSource { get; }
    protected ILogger Log { get; }

    public bool IsCancelled => DurationTask.IsCanceled;
    MediaFormat IMediaSource.Format => Format;
    public TFormat Format { get; }
    protected Task<TimeSpan> DurationTask => DurationTaskSource.Task;
    public TimeSpan Duration => DurationTask.IsCompleted
        ? DurationTask.GetAwaiter().GetResult()
        : throw StandardError.Unavailable("Duration isn't parsed yet.");
    public Task WhenDurationAvailable => DurationTask;
    public Moment CreatedAt { get; }

    protected MediaSource(
        Moment createdAt,
        TFormat format,
        IAsyncEnumerable<TFrame> frameStream,
        ILogger log,
        CancellationToken cancellationToken)
    {
        CreatedAt = createdAt;
        Format = format;
        DurationTaskSource = AsyncTaskMethodBuilderExt.New<TimeSpan>();
        // MediaFrame.Dispose() is a no-op in the current design — the memoizer holds
        // frames until GC reclaims them, so no eviction callback is needed.
        MemoizedFrames = IterateThrough(frameStream, cancellationToken)
            .Memoize(cancellationToken);
        Log = log;
    }

    public void Dispose()
        => MemoizedFrames.Dispose();

    // Public methods

    IAsyncEnumerable<MediaFrame> IMediaSource.GetFramesUntyped(CancellationToken cancellationToken)
        => GetFrames(cancellationToken);
    public IAsyncEnumerable<TFrame> GetFrames(CancellationToken cancellationToken)
        => MemoizedFrames.Replay(cancellationToken);

    // Protected & private methods

    private async IAsyncEnumerable<TFrame> IterateThrough(
        IAsyncEnumerable<TFrame> frames,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var isEmpty = true;
        var duration = TimeSpan.Zero;
        try {
            await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                isEmpty = false;
                duration = frame.Offset + frame.Duration;
                yield return frame;
            }
            DurationTaskSource.SetResult(duration);
        }
        finally {
            if (cancellationToken.IsCancellationRequested)
                DurationTaskSource.TrySetCanceled(cancellationToken);
            else {
                if (!DurationTask.IsCompleted) {
                    if (isEmpty)
                        DurationTaskSource.TrySetCanceled(cancellationToken);
                    else
                        DurationTaskSource.TrySetException(
                            new InvalidOperationException("MediaSource.IterateThrough: Duration wasn't parsed."));
                }
            }
        }
    }
}

namespace ActualChat.Media;

public abstract class MediaSource<TFormat, TFrame> : IMediaSource
    where TFormat : MediaFormat
    where TFrame : MediaFrame
{
    protected AsyncMemoizer<TFrame> MemoizedFrames { get; }
    protected TaskCompletionSource<TimeSpan> DurationTaskSource { get; }
    protected ILogger Log { get; }

    public bool IsCancelled => DurationTask.IsCanceled;
    MediaFormat IMediaSource.Format => Format;
    public TFormat Format { get; }
    protected Task<TimeSpan> DurationTask => DurationTaskSource.Task;
#pragma warning disable VSTHRD002
    public TimeSpan Duration => DurationTask.IsCompleted
        ? DurationTask.Result
        : throw StandardError.Unavailable("Duration isn't parsed yet.");
#pragma warning restore VSTHRD002
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
        DurationTaskSource = TaskCompletionSourceExt.New<TimeSpan>();
        MemoizedFrames = new AsyncMemoizer<TFrame>(IterateThrough(frameStream, cancellationToken), cancellationToken);
        Log = log;
    }

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

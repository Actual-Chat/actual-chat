namespace ActualChat.Media;

public interface IMediaSource
{
    bool IsCancelled { get; }
    MediaFormat Format { get; }
    TimeSpan Duration { get; }
    Task WhenFormatAvailable { get; }
    Task WhenDurationAvailable { get; }

    IAsyncEnumerable<MediaFrame> GetFramesUntyped(CancellationToken cancellationToken);
    IAsyncEnumerable<IMediaStreamPart> GetStreamUntyped(CancellationToken cancellationToken);
    IAsyncEnumerable<BlobPart> GetBlobStream(CancellationToken cancellationToken);
}

public abstract class MediaSource<TFormat, TFrame, TStreamPart> : IMediaSource
    where TFormat : MediaFormat
    where TFrame : MediaFrame
    where TStreamPart : class, IMediaStreamPart<TFormat, TFrame>, new()
{
    protected AsyncMemoizer<TFrame> MemoizedFrames { get; }
    protected Task<TFormat> FormatTask { get; }
    protected Task<TimeSpan> DurationTask { get; }
    protected ILogger Log { get; }
    protected abstract TFormat DefaultFormat { get; }

    public bool IsCancelled => FormatTask.IsCanceled;
    MediaFormat IMediaSource.Format => Format;
#pragma warning disable VSTHRD002
    public TFormat Format => FormatTask.IsCompleted
        ? FormatTask.Result
        : throw new InvalidOperationException("Format isn't parsed yet.");
    public TimeSpan Duration => DurationTask.IsCompleted
        ? DurationTask.Result
        : throw new InvalidOperationException("Duration isn't parsed yet.");
#pragma warning restore VSTHRD002
    public Task WhenFormatAvailable => FormatTask;
    public Task WhenDurationAvailable => DurationTask;

    // Constructors

#pragma warning disable MA0056
    protected MediaSource(
        IAsyncEnumerable<BlobPart> blobStream,
        TimeSpan skipTo,
        ILogger log,
        CancellationToken cancellationToken)
    {
        Log = log;
        FormatTask = TaskSource.New<TFormat>(true).Task;
        DurationTask = TaskSource.New<TimeSpan>(true).Task;
        // ReSharper disable once VirtualMemberCallInConstructor
        var parsedFrames = Parse(blobStream, skipTo, cancellationToken);
        MemoizedFrames = new AsyncMemoizer<TFrame>(parsedFrames, cancellationToken);
    }

    protected MediaSource(
        IAsyncEnumerable<IMediaStreamPart> mediaStream,
        ILogger log,
        CancellationToken cancellationToken)
    {
        Log = log;
        FormatTask = TaskSource.New<TFormat>(true).Task;
        DurationTask = TaskSource.New<TimeSpan>(true).Task;
        // ReSharper disable once VirtualMemberCallInConstructor
        var parsedFrames = Parse(mediaStream, cancellationToken);
        MemoizedFrames = new AsyncMemoizer<TFrame>(parsedFrames, cancellationToken);
    }
#pragma warning restore MA0056

    // Public methods

    IAsyncEnumerable<MediaFrame> IMediaSource.GetFramesUntyped(CancellationToken cancellationToken)
        => GetFrames(cancellationToken);
    public IAsyncEnumerable<TFrame> GetFrames(CancellationToken cancellationToken)
        => MemoizedFrames.Replay(cancellationToken);

    IAsyncEnumerable<IMediaStreamPart> IMediaSource.GetStreamUntyped(CancellationToken cancellationToken)
        => GetStream(cancellationToken);
    public async IAsyncEnumerable<TStreamPart> GetStream(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!WhenDurationAvailable.IsCompleted)
            await WhenFormatAvailable.WithFakeCancellation(cancellationToken).ConfigureAwait(false);
        yield return new TStreamPart { Format = Format };
        await foreach (var frame in GetFrames(cancellationToken).ConfigureAwait(false))
            yield return new TStreamPart { Frame = frame };
    }

    public IAsyncEnumerable<BlobPart> GetBlobStream(CancellationToken cancellationToken)
        => GetStream(cancellationToken).ToBlobStream(cancellationToken);

    // Protected & private methods

    protected abstract IAsyncEnumerable<TFrame> Parse(
        IAsyncEnumerable<BlobPart> blobStream,
        TimeSpan skipTo,
        CancellationToken cancellationToken);

    protected virtual async IAsyncEnumerable<TFrame> Parse(
        IAsyncEnumerable<IMediaStreamPart> mediaStream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var isEmpty = true;
        var duration = TimeSpan.Zero;
        var formatTaskSource = TaskSource.For(FormatTask);
        var durationTaskSource = TaskSource.For(DurationTask);
        try {
            await foreach (var mediaStreamPart in mediaStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                // ReSharper disable once HeapView.PossibleBoxingAllocation
                isEmpty = false;
                var part = (TStreamPart) mediaStreamPart;
                var (format, frame) = (part.Format, part.Frame);
                if (FormatTask.IsCompleted) {
                    if (format != null)
                        throw new InvalidOperationException("Format part must be the first one.");
                    if (frame != null) {
                        duration = frame.Offset + frame.Duration;
                        yield return frame;
                    }
                    else
                        throw new InvalidOperationException("MediaStreamPart doesn't have any properties set.");
                }
                else
                    formatTaskSource.SetResult(format ?? DefaultFormat);
            }
            durationTaskSource.SetResult(duration);
        }
        finally {
            if (cancellationToken.IsCancellationRequested) {
                formatTaskSource.TrySetCanceled(cancellationToken);
                durationTaskSource.TrySetCanceled(cancellationToken);
            }
            else {
                if (!FormatTask.IsCompleted) {
                    if (isEmpty)
                        formatTaskSource.TrySetCanceled(cancellationToken);
                    else
                        formatTaskSource.TrySetException(
                            new InvalidOperationException("MediaSource.Parse: Format wasn't parsed."));
                }
                if (!DurationTask.IsCompleted) {
                    if (isEmpty)
                        durationTaskSource.TrySetCanceled(cancellationToken);
                    else
                        durationTaskSource.TrySetException(
                            new InvalidOperationException("MediaSource.Parse: Duration wasn't parsed."));
                }
            }
        }
    }
}

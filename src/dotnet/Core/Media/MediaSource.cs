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
    IAsyncEnumerable<byte[]> GetBlobStream(CancellationToken cancellationToken);
}

public abstract class MediaSource<TFormat, TFrame, TStreamPart, TMetadata> : IMediaSource
    where TFormat : MediaFormat
    where TFrame : MediaFrame
    where TStreamPart : class, IMediaStreamPart<TFormat, TFrame>, new()
    where TMetadata : class
{
    protected AsyncMemoizer<TFrame> MemoizedFrames { get; }
    protected Task<TFormat> FormatTask { get; }
    protected Task<TimeSpan> DurationTask { get; }
    protected Task<TMetadata> MetadataTask { get; }
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
    public TMetadata Metadata => MetadataTask.IsCompleted
        ? MetadataTask.Result
        : throw new InvalidOperationException("Metadata weren't parsed yet.");
#pragma warning restore VSTHRD002
    public Task WhenFormatAvailable => FormatTask;
    public Task WhenDurationAvailable => DurationTask;

    // Constructors

#pragma warning disable MA0056
    protected MediaSource(
        IAsyncEnumerable<byte[]> blobStream,
        TMetadata metadata,
        TimeSpan skipTo,
        ILogger log,
        CancellationToken cancellationToken)
    {
        Log = log;
        FormatTask = TaskSource.New<TFormat>(true).Task;
        DurationTask = TaskSource.New<TimeSpan>(true).Task;
        MetadataTask =  TaskSource.New<TMetadata>(true).Task;
        // ReSharper disable once VirtualMemberCallInConstructor
        var parsedFrames = Parse(
            blobStream.Select(blob => new RecordingPart { Data = blob }),
            metadata,
            skipTo,
            cancellationToken);
        MemoizedFrames = new AsyncMemoizer<TFrame>(parsedFrames, cancellationToken);
    }

    protected MediaSource(
        IAsyncEnumerable<RecordingPart> recordingStream,
        TimeSpan skipTo,
        ILogger log,
        CancellationToken cancellationToken)
    {
        Log = log;
        FormatTask = TaskSource.New<TFormat>(true).Task;
        DurationTask = TaskSource.New<TimeSpan>(true).Task;
        MetadataTask = TaskSource.New<TMetadata>(true).Task;
        // ReSharper disable once VirtualMemberCallInConstructor
        var parsedFrames = Parse(recordingStream, null, skipTo, cancellationToken);
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
        MetadataTask = TaskSource.New<TMetadata>(true).Task;
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
        if (!WhenFormatAvailable.IsCompleted)
            await WhenFormatAvailable.WithFakeCancellation(cancellationToken).ConfigureAwait(false);
        yield return new TStreamPart { Format = Format };
        await foreach (var frame in GetFrames(cancellationToken).ConfigureAwait(false))
            yield return new TStreamPart { Frame = frame };
    }

    public IAsyncEnumerable<byte[]> GetBlobStream(CancellationToken cancellationToken)
        => GetStream(cancellationToken).ToBlobStream(cancellationToken);

    // Protected & private methods

    protected abstract IAsyncEnumerable<TFrame> Parse(
        IAsyncEnumerable<RecordingPart> recordingStream,
        TMetadata? metadata,
        TimeSpan skipTo,
        CancellationToken cancellationToken);

    protected abstract IAsyncEnumerable<TFrame> Parse(
        IAsyncEnumerable<IMediaStreamPart> mediaStream,
        CancellationToken cancellationToken);

}

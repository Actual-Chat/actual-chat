using ActualChat.Blobs;
using Stl.Reflection;

namespace ActualChat.Media;

public interface IMediaSource
{
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

    MediaFormat IMediaSource.Format => Format;
    public TFormat Format => FormatTask.IsCompleted
        ? FormatTask.Result
        : throw new InvalidOperationException("Format isn't parsed yet.");
    public TimeSpan Duration => DurationTask.IsCompleted
        ? DurationTask.Result
        : throw new InvalidOperationException("Duration isn't parsed yet.");
    public Task WhenFormatAvailable => FormatTask;
    public Task WhenDurationAvailable => DurationTask;

    // Constructors

    protected MediaSource(IAsyncEnumerable<BlobPart> blobStream, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        FormatTask = TaskSource.New<TFormat>(true).Task;
        DurationTask = TaskSource.New<TimeSpan>(true).Task;
        // ReSharper disable once VirtualMemberCallInConstructor
        var parsedFrames = Parse(blobStream, skipTo, cancellationToken);
        MemoizedFrames = new AsyncMemoizer<TFrame>(parsedFrames, cancellationToken);
    }

    protected MediaSource(Task<TFormat> formatTask, IAsyncEnumerable<TFrame> frames, CancellationToken cancellationToken)
    {
        FormatTask = TaskSource.New<TFormat>(true).Task;
        DurationTask = TaskSource.New<TimeSpan>(true).Task;
        // ReSharper disable once VirtualMemberCallInConstructor
        var parsedFrames = Parse(formatTask, frames, cancellationToken);
        MemoizedFrames = new AsyncMemoizer<TFrame>(parsedFrames, cancellationToken);
    }

    protected MediaSource(IAsyncEnumerable<IMediaStreamPart> mediaStream, CancellationToken cancellationToken)
    {
        FormatTask = TaskSource.New<TFormat>(true).Task;
        DurationTask = TaskSource.New<TimeSpan>(true).Task;
        // ReSharper disable once VirtualMemberCallInConstructor
        var parsedFrames = Parse(mediaStream, cancellationToken);
        MemoizedFrames = new AsyncMemoizer<TFrame>(parsedFrames, cancellationToken);
    }

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
        await foreach (var frame in GetFrames(cancellationToken).WithCancellation(cancellationToken))
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
        Task<TFormat> formatTask,
        IAsyncEnumerable<TFrame> frames,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var duration = TimeSpan.Zero;
        var formatTaskSource = TaskSource.For(FormatTask);
        var durationTaskSource = TaskSource.For(DurationTask);

        try {
            var format = await formatTask.WithFakeCancellation(cancellationToken).ConfigureAwait(false);
            formatTaskSource.SetResult(format);
            await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                duration = frame.Offset + frame.Duration;
                yield return frame;
            }
            durationTaskSource.SetResult(duration);
        }
        finally {
            if (cancellationToken.IsCancellationRequested) {
                formatTaskSource.TrySetCanceled(cancellationToken);
                durationTaskSource.TrySetCanceled(cancellationToken);
            }
            else {
                if (!FormatTask.IsCompleted)
                    formatTaskSource.TrySetException(new InvalidOperationException("Format wasn't parsed."));
                if (!DurationTask.IsCompleted)
                    durationTaskSource.TrySetException(new InvalidOperationException("Duration wasn't parsed."));
            }
        }
    }

    protected virtual async IAsyncEnumerable<TFrame> Parse(
        IAsyncEnumerable<IMediaStreamPart> mediaStream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var duration = TimeSpan.Zero;
        var formatTaskSource = TaskSource.For(FormatTask);
        var durationTaskSource = TaskSource.For(DurationTask);
        try {
            await foreach (var mediaStreamPart in mediaStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                // ReSharper disable once HeapView.PossibleBoxingAllocation
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
                else {
                    if (format == null)
                        throw new InvalidOperationException("Format part is expected first.");
                    formatTaskSource.SetResult(format);
                }
            }
            durationTaskSource.SetResult(duration);
        }
        finally {
            if (cancellationToken.IsCancellationRequested) {
                formatTaskSource.TrySetCanceled(cancellationToken);
                durationTaskSource.TrySetCanceled(cancellationToken);
            }
            else {
                if (!FormatTask.IsCompleted)
                    formatTaskSource.TrySetException(new InvalidOperationException("Format wasn't parsed."));
                if (!DurationTask.IsCompleted)
                    durationTaskSource.TrySetException(new InvalidOperationException("Duration wasn't parsed."));
            }
        }
    }
}

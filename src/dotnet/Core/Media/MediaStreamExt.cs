namespace ActualChat.Media;

public static class MediaStreamExt
{
    public static  (Task<TMediaFormat> FormatTask, IAsyncEnumerable<TMediaFrame> Frames) ToMediaFrames<TMediaFormat, TMediaFrame>(
        this IAsyncEnumerable<IMediaStreamPart<TMediaFormat, TMediaFrame>> mediaStream,
        CancellationToken cancellationToken)
        where TMediaFormat: MediaFormat
    {
        var format = new TaskCompletionSource<TMediaFormat>();
        return (format.Task, ConvertToFrames(mediaStream, format, cancellationToken));
    }

    public static async IAsyncEnumerable<byte[]> ToByteStream<TMediaFormat>(
        this IAsyncEnumerable<MediaFrame> mediaStream,
        Task<TMediaFormat> formatTask,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TMediaFormat: MediaFormat
    {
        var format = await formatTask.WithFakeCancellation(cancellationToken).ConfigureAwait(false);
        yield return format.Serialize();

        await foreach (var frame in mediaStream.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return frame.Data;
    }

    public static async IAsyncEnumerable<byte[]> ToByteStream<TMediaFormat>(
        this IAsyncEnumerable<MediaFrame> mediaStream,
        TMediaFormat format,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TMediaFormat: MediaFormat
    {
        yield return format.Serialize();

        await foreach (var frame in mediaStream.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return frame.Data;
    }

    private static async IAsyncEnumerable<TMediaFrame> ConvertToFrames<TMediaFormat, TMediaFrame>(
        IAsyncEnumerable<IMediaStreamPart<TMediaFormat, TMediaFrame>> mediaStream,
        TaskCompletionSource<TMediaFormat> format,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var first = true;
        await foreach (var part in mediaStream.WithCancellation(cancellationToken).ConfigureAwait(false))
            if (first) {
                if (part.Format == null)
                    throw new InvalidOperationException("Format part should be the first one.");

                format.SetResult(part.Format);
                first = false;
            }
            else {
                if (format.Task.IsCompleted && part.Format != null)
                    throw new InvalidOperationException("Format part can't repeat.");
                if (part.Frame == null)
                    throw new InvalidOperationException("Frame should be specified for all parts except the first one.");

                yield return part.Frame;
            }
    }
}

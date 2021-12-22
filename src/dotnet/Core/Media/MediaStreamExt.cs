namespace ActualChat.Media;

public static class MediaStreamExt
{
    public static async IAsyncEnumerable<byte[]> ToBlobStream<TMediaStreamPart>(
        this IAsyncEnumerable<TMediaStreamPart> mediaStream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TMediaStreamPart : IMediaStreamPart
    {
        byte[]? header = null;
        var first = true;
        await foreach (var part in mediaStream.WithCancellation(cancellationToken).ConfigureAwait(false))
            if (first) {
                if (part.Format == null)
                    throw new InvalidOperationException("Format part should be the first one.");

                header = part.Format.Serialize(); // We'll yield it w/ the first Blob
                yield return header;
                first = false;
            }
            else {
                if (header != null && part.Format != null)
                    throw new InvalidOperationException("Format part can't repeat.");
                if (part.Frame == null)
                    throw new InvalidOperationException("Frame should be specified for all parts except the first one.");

                yield return part.Frame.Data;
            }
    }
}

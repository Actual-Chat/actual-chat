namespace ActualChat.Media;

public static class MediaStreamExt
{
    public static async IAsyncEnumerable<BlobPart> ToBlobStream<TMediaStreamPart>(
        this IAsyncEnumerable<TMediaStreamPart> mediaStream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TMediaStreamPart : IMediaStreamPart
    {
        BlobPart? header = null;
        var index = 0;
        await foreach (var part in mediaStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            if (part.Format != null) {
                if (header != null)
                    throw new InvalidOperationException("Format part can't repeat.");
                header = part.Format.ToBlobPart(); // We'll yield it w/ the first BlobPart
            }
            else if (part.Frame != null) {
                if (header == null)
                    throw new InvalidOperationException("Format part should be the first one.");
                var blobPart = part.Frame.ToBlobPart(index++);
                if (index == 1) // First BlobPart must include header
                    blobPart = new BlobPart(0, header.Data, blobPart.Data);
                yield return blobPart;
            }
        }
    }
}

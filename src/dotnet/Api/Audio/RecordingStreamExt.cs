using ActualChat.Media;

namespace ActualChat.Audio;

public static class RecordingStreamExt
{
    public static async IAsyncEnumerable<RecordingPart> ToRecordingStream(
        this IAsyncEnumerable<byte[]> byteStream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new RecordingPart(RecordingEventKind.Resume) {
            RecordedAt = SystemClock.Now,
            Offset = TimeSpan.Zero,
        };

        await foreach (var chunk in byteStream.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return new RecordingPart(RecordingEventKind.Data) { Data = chunk };
    }
}

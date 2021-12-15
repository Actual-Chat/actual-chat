namespace ActualChat.Audio;

public static class AudioStreamExt
{
    public static async IAsyncEnumerable<AudioStreamPart> ExtractFormat(
        this IAsyncEnumerable<AudioStreamPart> stream,
        TaskSource<AudioFormat> formatTaskSource,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try {
            var index = 0;
            await foreach (var part in stream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                if (index == 0) {
                    if (part.Format != null)
                        formatTaskSource.SetResult(part.Format);
                    else
                        throw AudioStreamShouldStartWithFormatPartError();
                }
                else {
                    if (!formatTaskSource.Task.IsCompleted)
                        throw AudioStreamShouldStartWithFormatPartError();
                }
                yield return part;
                index++;
            }
        }
        finally {
            formatTaskSource.TrySetException(
                new InvalidOperationException("AudioStream should start with Format part."));
        }
    }

    public static async Task<(AudioFormat Format, IAsyncEnumerator<AudioStreamPart> Frames)> ToFormatAndFrames(
        this IAsyncEnumerable<AudioStreamPart> stream,
        CancellationToken cancellationToken = default)
    {
        var e = stream.GetAsyncEnumerator(cancellationToken).WithCancellation(cancellationToken);
        if (!await e.MoveNextAsync().ConfigureAwait(false))
            throw AudioStreamShouldStartWithFormatPartError();
        var format = e.Current.Format;
        if (format == null)
            throw AudioStreamShouldStartWithFormatPartError();
        return (format, e);
    }

    private static Exception AudioStreamShouldStartWithFormatPartError()
        => new InvalidOperationException("AudioStream should start with the Format part.");
}

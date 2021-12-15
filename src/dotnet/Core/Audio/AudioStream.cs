namespace ActualChat.Audio;

public static class AudioStream
{
    public static async IAsyncEnumerable<AudioStreamPart> New(
        AudioFormat format,
        IAsyncEnumerable<AudioFrame> frames,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new AudioStreamPart(format);
        await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return new AudioStreamPart(frame);
    }

    public static async IAsyncEnumerable<AudioStreamPart> New(
        AudioFormat format,
        IAsyncEnumerable<AudioStreamPart> frameParts,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new AudioStreamPart(format);
        await foreach (var framePart in frameParts.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return framePart;
    }
}

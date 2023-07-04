namespace ActualChat.Audio;

public static class AudioSourceExt
{
    public static AudioSource Concat(
        this AudioSource first,
        AudioSource second,
        CancellationToken cancellationToken = default)
        => new (
            first.CreatedAt,
            first.Format,
            first.GetFrames(cancellationToken).Concat(second.GetFrames(cancellationToken)),
            TimeSpan.Zero,
            first.Log,
            cancellationToken);

    public static AudioSource ConcatUntil(
        this AudioSource first,
        AudioSource second,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
        => new (
            first.CreatedAt,
            first.Format,
            first.GetFrames(cancellationToken).ConcatUntil(second.GetFrames(cancellationToken), duration),
            TimeSpan.Zero,
            first.Log,
            cancellationToken);

    public static AudioSource Take(
        this AudioSource source,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
        => new (
            source.CreatedAt,
            source.Format,
            source.GetFrames(cancellationToken).TakeWhile(f => f.Offset < duration),
            TimeSpan.Zero,
            source.Log,
            cancellationToken);

    public static async IAsyncEnumerable<AudioFrame> Concat(
        this IAsyncEnumerable<AudioFrame> first,
        IAsyncEnumerable<AudioFrame> second)
    {
        var nextOffset = TimeSpan.Zero;
        await foreach (var frame in first.ConfigureAwait(false)) {
            nextOffset = frame.Offset + frame.Duration;
            yield return frame;
        }
        await foreach (var frame in second.ConfigureAwait(false)) {
            var offset = frame.Offset + nextOffset;
            yield return new AudioFrame {
                Offset = offset,
                Data = frame.Data,
            };
        }
    }

    public static async IAsyncEnumerable<AudioFrame> ConcatUntil(
        this IAsyncEnumerable<AudioFrame> first,
        IAsyncEnumerable<AudioFrame> second,
        TimeSpan duration)
    {
        var nextOffset = TimeSpan.Zero;
        await foreach (var frame in first.ConfigureAwait(false)) {
            nextOffset = frame.Offset + frame.Duration;
            yield return frame;
        }
        await foreach (var frame in second.ConfigureAwait(false)) {
            var offset = frame.Offset + nextOffset;
            if (offset > duration)
                yield break;

            yield return new AudioFrame {
                Offset = offset,
                Data = frame.Data,
            };
        }
    }
}

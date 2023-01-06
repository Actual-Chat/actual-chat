namespace ActualChat.Audio;

public static class AudioSourceExt
{
    public static AudioSource Concat(this AudioSource left, AudioSource right, CancellationToken cancellationToken)
        => new (
            left.FormatTask,
            left.GetFrames(cancellationToken).Concat(right.GetFrames(cancellationToken)),
            TimeSpan.Zero,
            left.Log,
            cancellationToken);

    public static AudioSource Take(this AudioSource left, TimeSpan duration, CancellationToken cancellationToken)
        => new (
            left.FormatTask,
            left.GetFrames(cancellationToken).TakeWhile(f => f.Offset < duration),
            TimeSpan.Zero,
            left.Log,
            cancellationToken);

    public static async IAsyncEnumerable<AudioFrame> Concat(
        this IAsyncEnumerable<AudioFrame> left,
        IAsyncEnumerable<AudioFrame> right)
    {
        var nextOffset = 0L;
        await foreach (var frame in left.ConfigureAwait(false)) {
            nextOffset = frame.Offset.Ticks + frame.Duration.Ticks;
            yield return frame;
        }
        await foreach (var frame in right.ConfigureAwait(false)) {
            var offset = frame.Offset.Add(TimeSpan.FromTicks(nextOffset));
            yield return new AudioFrame {
                Offset = offset,
                Data = frame.Data,
            };
        }
    }
}

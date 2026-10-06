namespace ActualChat;

public static partial class AsyncEnumerableExt
{
    private static readonly BoundedChannelOptions ThrottleChannelOptions = new(1) {
        SingleReader = true,
        SingleWriter = true,
        AllowSynchronousContinuations = true,
        FullMode = BoundedChannelFullMode.DropOldest,
    };

    public static IAsyncEnumerable<T> Throttle<T>(
        this IAsyncEnumerable<T> source,
        TimeSpan minInterval,
        CancellationToken cancellationToken = default)
        => source.Throttle(minInterval, MomentClockSet.Default.CpuClock, cancellationToken);

    public static async IAsyncEnumerable<T> Throttle<T>(
        this IAsyncEnumerable<T> source,
        TimeSpan minInterval,
        MomentClock clock,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        var c = ThrottleChannelOptions.NewChannel<T>();
        _ = source.CopyTo(c, ChannelCopyMode.CopyAllSilently, cancellationToken);
        await foreach (var item in c.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
            yield return item;

            if (minInterval > TimeSpan.Zero)
                await clock.Delay(minInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}

namespace ActualChat;

public static partial class ChannelExt
{
    // ProcessConcurrently

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task ProcessConcurrently<T>(
        this Channel<T> source,
        int concurrencyLevel,
        Func<T, CancellationToken, ValueTask> processor,
        CancellationToken cancellationToken = default)
        => source.Reader.ProcessConcurrently(concurrencyLevel, processor, cancellationToken);

    public static async Task ProcessConcurrently<T>(
        this ChannelReader<T> source,
        int concurrencyLevel,
        Func<T, CancellationToken, ValueTask> processor,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrencyLevel);

        var tasks = new Task[concurrencyLevel];
        for (var i = 0; i < tasks.Length; i++)
            tasks[i] = Task.Run(Worker, cancellationToken);

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return;

        async Task Worker() {
            while (await source.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                while (source.TryRead(out var item))
                    try {
                        await processor.Invoke(item, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                        // Intended: we assume the processor handles every exception,
                        // but the worker shouldn't break on somehow unprocessed exception anyway.
                    }
        }
    }

    // Split

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task Split<T>(
        this Channel<T> source,
        IReadOnlyList<Channel<T>> destinations,
        Func<T, int> channelIndexSelector,
        CancellationToken cancellationToken = default)
        => source.Reader.Split(destinations, channelIndexSelector, cancellationToken);

    public static Task Split<T>(
        this ChannelReader<T> source,
        IReadOnlyList<Channel<T>> destinations,
        Func<T, int> channelIndexSelector,
        CancellationToken cancellationToken = default)
    {
        return destinations.Count > 0
            ? Task.Run(Pump, cancellationToken)
            : throw new ArgumentOutOfRangeException(nameof(destinations));

        async Task Pump() {
            Exception? error = null;
            try {
                while (await source.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    while (source.TryRead(out var item)) {
                        var channelIndex = channelIndexSelector.Invoke(item);
                        if ((uint)channelIndex >= (uint)destinations.Count) // Faster check that covers <= 0 as well
                            throw new ArgumentOutOfRangeException(
                                nameof(channelIndexSelector),
                                $"Invalid channel index: {channelIndex}");
                        await destinations[channelIndex].Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                    }
            }
            catch (Exception e) {
                error = e;
                throw;
            }
            finally {
                foreach (var destination in destinations)
                    destination.Writer.TryComplete(error);
            }
        }
    }
}

namespace ActualChat;

/// <summary>
/// Batched delayed disposal using a single worker + channel.
/// Items are queued with a timestamp on eviction; the worker disposes them
/// after <see cref="Delay"/> has elapsed. Zero per-item allocation — no Task.Delay/ContinueWith.
/// Items must be evicted in roughly chronological order (FIFO processing).
/// </summary>
public sealed class DelayedDisposer<T>(TimeSpan delay) : WorkerBase where T : IDisposable
{
    private readonly Channel<(T Item, long EvictedAt)> _channel =
        Channel.CreateUnbounded<(T, long)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Queue<(T Item, long EvictedAt)> _pending = new();

    public TimeSpan Delay { get; } = delay;

    /// <summary>
    /// Queue an item for delayed disposal. Called from memoizer's OnRemove callback.
    /// </summary>
    public void Add(T item)
        => _channel.Writer.TryWrite((item, Stopwatch.GetTimestamp()));

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var reader = _channel.Reader;
        // Reusable CTS for timeout waits — avoids allocation per wake-up
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try {
            while (!cancellationToken.IsCancellationRequested) {
                // 1. Drain channel into pending queue
                while (reader.TryRead(out var entry))
                    _pending.Enqueue(entry);

                // 2. Dispose items whose delay has expired
                while (_pending.TryPeek(out var head)
                       && Stopwatch.GetElapsedTime(head.EvictedAt) >= Delay) {
                    _pending.Dequeue();
                    head.Item.Dispose();
                }

                // 3. Wait for next event: new item arrives OR earliest pending item expires
                if (_pending.TryPeek(out var next)) {
                    var remaining = Delay - Stopwatch.GetElapsedTime(next.EvictedAt);
                    if (remaining <= CoreConstants.DelayedDisposer.MinWait)
                        continue;

                    if (!timeoutCts.TryReset())
                        timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(remaining);
                    try {
                        await reader.WaitToReadAsync(timeoutCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                        // Timeout — next item is due, loop to dispose it
                    }
                }
                else {
                    // Nothing pending — wait for any new item
                    if (!await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                        break;
                }
            }
        }
        finally {
            timeoutCts.Dispose();
        }
    }

    protected override Task OnStop()
    {
        _channel.Writer.TryComplete();

        // Dispose remaining items immediately
        while (_pending.TryDequeue(out var entry))
            entry.Item.Dispose();
        while (_channel.Reader.TryRead(out var entry))
            entry.Item.Dispose();

        return Task.CompletedTask;
    }
}

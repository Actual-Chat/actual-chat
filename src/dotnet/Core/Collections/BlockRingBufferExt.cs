namespace ActualChat.Collections;

public static class BlockRingBufferExt
{
    public static ValueTask<ReadOnlyMemory<T>> Read<T>(
        this BlockRingBuffer<T> buffer,
        int maxLength,
        CancellationToken cancellationToken = default)
    {
        if (buffer.TryRead(maxLength, out var data, out var whenReady))
            return new ValueTask<ReadOnlyMemory<T>>(data);

        return CompleteAsync(buffer, maxLength, whenReady, cancellationToken);

        static async ValueTask<ReadOnlyMemory<T>> CompleteAsync(
            BlockRingBuffer<T> buffer,
            int maxLength,
            Task whenReady,
            CancellationToken cancellationToken)
        {
            await whenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            while (true) {
                if (buffer.TryRead(maxLength, out var data, out var nextReady))
                    return data;
                await nextReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static async IAsyncEnumerable<ReadOnlyMemory<T>> ReadAll<T>(
        this BlockRingBuffer<T> buffer,
        int maxChunkSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true) {
            if (!buffer.TryRead(maxChunkSize, out var data, out var whenReady)) {
                await whenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }
            yield return data;
        }
        // ReSharper disable once IteratorNeverReturns
    }
}

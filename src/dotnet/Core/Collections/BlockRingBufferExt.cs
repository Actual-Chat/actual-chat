namespace ActualChat.Collections;

public static class BlockRingBufferExt
{
    public static async Task Write<T>(
        this BlockRingBuffer<T> buffer,
        ReadOnlyMemory<T> data,
        CancellationToken cancellationToken = default)
    {
        var remaining = data;
        while (remaining.Length > 0) {
            if (buffer.TryWrite(remaining.Span, out var written, out var whenReady))
                return;
            remaining = remaining[written..];
            await whenReady!.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

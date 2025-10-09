using System.Buffers;

namespace ActualChat.Collections;

public static class BlockRingBufferExt
{
    public static async IAsyncEnumerable<IMemoryOwner<T>> PullAll<T>(
        this BlockRingBuffer<T> buffer,
        int chunkSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            if (!buffer.TryPull(chunkSize, out var frame)) {
                await buffer.WhenPushed.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }
            yield return frame; // ownership transferred to consumer
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
}

namespace ActualChat.Internal;

public class AsyncEnumerableOnce<T>(IAsyncEnumerator<T> enumerator, bool suppressDispose = false)
    : IAsyncEnumerable<T>
{
    private int _getAsyncEnumeratorCount;

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var isUsed = Interlocked.CompareExchange(ref _getAsyncEnumeratorCount, 1, 0) != 0;
        if (isUsed)
            throw StandardError.StateTransition(
                $"{nameof(GetAsyncEnumerator)} can be called just once on this sequence.");
        try {
            cancellationToken.ThrowIfCancellationRequested();
            while (await enumerator.MoveNextAsync().ConfigureAwait(false)) {
                yield return enumerator.Current;
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally {
            if (!suppressDispose)
                await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }
}

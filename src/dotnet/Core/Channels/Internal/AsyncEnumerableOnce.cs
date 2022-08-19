namespace ActualChat.Channels.Internal;

public class AsyncEnumerableOnce<T> : IAsyncEnumerable<T>
{
    private int _getAsyncEnumeratorCount;
    private readonly IAsyncEnumerator<T> _enumerator;
    private readonly bool _suppressDispose;

    public AsyncEnumerableOnce(IAsyncEnumerator<T> enumerator, bool suppressDispose = false)
    {
        _enumerator = enumerator;
        _suppressDispose = suppressDispose;
    }

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var isUsed = Interlocked.CompareExchange(ref _getAsyncEnumeratorCount, 1, 0) != 0;
        if (isUsed)
            throw StandardError.StateTransition(
                $"{nameof(GetAsyncEnumerator)} can be called just once on this sequence.");
        try {
            cancellationToken.ThrowIfCancellationRequested();
            while (await _enumerator.MoveNextAsync().ConfigureAwait(false)) {
                yield return _enumerator.Current;
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally {
            if (!_suppressDispose)
                await _enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }
}

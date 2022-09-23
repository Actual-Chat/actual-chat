namespace ActualChat.Channels;

public sealed class AsyncEnumerableWithUsedEnumerator<T> : IAsyncEnumerable<T>
{
    private readonly IAsyncEnumerable<T> _enumerable;
    private IAsyncEnumerator<T>? _usedEnumerator;
    private readonly bool _hasCurrent;

    public AsyncEnumerableWithUsedEnumerator(IAsyncEnumerable<T> enumerable, IAsyncEnumerator<T> usedEnumerator, bool hasCurrent)
    {
        _enumerable = enumerable;
        _usedEnumerator = usedEnumerator;
        _hasCurrent = hasCurrent;
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var usedEnumerator = Interlocked.Exchange(ref _usedEnumerator, null);
        return usedEnumerator != null
            ? ReuseEnumerator(usedEnumerator, _hasCurrent)
            : _enumerable.GetAsyncEnumerator(cancellationToken);
    }

    private static async IAsyncEnumerator<T> ReuseEnumerator(IAsyncEnumerator<T> usedEnumerator, bool hasCurrent)
    {
        try {
            if (!hasCurrent)
                yield break;

            yield return usedEnumerator.Current;

            while (await usedEnumerator.MoveNextAsync().ConfigureAwait(false))
                yield return usedEnumerator.Current;
        }
        finally {
            await usedEnumerator.DisposeAsync().ConfigureAwait(false);
        }
    }
}

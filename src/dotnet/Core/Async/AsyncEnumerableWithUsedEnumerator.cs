namespace ActualChat;

public sealed class AsyncEnumerableWithUsedEnumerator<T>(
    IAsyncEnumerable<T> enumerable,
    IAsyncEnumerator<T> usedEnumerator,
    bool hasCurrent
    ) : IAsyncEnumerable<T>
{
    private IAsyncEnumerator<T>? _usedEnumerator = usedEnumerator;

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var usedEnumerator1 = Interlocked.Exchange(ref _usedEnumerator, null);
        return usedEnumerator1 != null
            ? ReuseEnumerator(usedEnumerator1, hasCurrent)
            : enumerable.GetAsyncEnumerator(cancellationToken);
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

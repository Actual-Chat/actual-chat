namespace ActualChat;

public static class ComputedExt
{
    public static async Task<IComputed<T>> When<T>(this IComputed<T> computed,
        Func<T, bool> predicate,
        CancellationToken cancellationToken = default)
    {
        while (true) {
            if (!computed.IsConsistent())
                computed = await computed.Update(cancellationToken).ConfigureAwait(false);
            if (predicate(computed.Value))
                return computed;
            await computed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
        }
    }

    public static async IAsyncEnumerable<IComputed<T>> Changes<T>(
        this IComputed<T> computed,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true) {
            computed = await computed.Update(cancellationToken).ConfigureAwait(false);
            yield return computed;
            await computed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
        }
        // ReSharper disable once IteratorNeverReturns
    }
}

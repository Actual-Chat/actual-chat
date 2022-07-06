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

    public static async ValueTask<(IComputed<T1>, IComputed<T2>)> Update<T1, T2>(
        IComputed<T1> c1,
        IComputed<T2> c2,
        CancellationToken cancellationToken = default)
    {
        while (true) {
            var t1 = c1.IsConsistent() ? null : c1.Update(cancellationToken).AsTask();
            var t2 = c2.IsConsistent() ? null : c2.Update(cancellationToken).AsTask();
            if (t1 is null && t2 is null)
                return (c1, c2);
            await Task.WhenAll(t1 ?? Task.CompletedTask, t2 ?? Task.CompletedTask)
                .ConfigureAwait(false);
#pragma warning disable MA0004
            c1 = t1 is null ? c1 : await t1;
            c2 = t2 is null ? c2 : await t2;
#pragma warning restore MA0004
        }
    }

    public static async ValueTask<(IComputed<T1>, IComputed<T2>, IComputed<T3>)> Update<T1, T2, T3>(
        IComputed<T1> c1,
        IComputed<T2> c2,
        IComputed<T3> c3,
        CancellationToken cancellationToken = default)
    {
        while (true) {
            var t1 = c1.IsConsistent() ? null : c1.Update(cancellationToken).AsTask();
            var t2 = c2.IsConsistent() ? null : c2.Update(cancellationToken).AsTask();
            var t3 = c3.IsConsistent() ? null : c3.Update(cancellationToken).AsTask();
            if (t1 is null && t2 is null && t3 is null)
                return (c1, c2, c3);
            await Task.WhenAll(t1 ?? Task.CompletedTask, t2 ?? Task.CompletedTask, t3 ?? Task.CompletedTask)
                .ConfigureAwait(false);
#pragma warning disable MA0004
            c1 = t1 is null ? c1 : await t1;
            c2 = t2 is null ? c2 : await t2;
            c3 = t3 is null ? c3 : await t3;
#pragma warning restore MA0004
        }
    }
}

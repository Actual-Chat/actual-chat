namespace ActualChat;

public static class ComputedExt
{
    public static async Task When<T>(this IComputed<T> computed,
        Func<T, bool> predicate,
        CancellationToken cancellationToken = default)
    {
        while (true) {
            if (!computed.IsConsistent())
                computed = await computed.Update(cancellationToken).ConfigureAwait(false);
            if (predicate(computed.Value))
                return;
            await computed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
        }
    }
}

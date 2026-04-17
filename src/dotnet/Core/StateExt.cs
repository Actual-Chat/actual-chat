namespace ActualChat;

public static class StateExt
{
    // TODO: remove when KvasSyncedState.Use() waits for first read
    public static async Task<T> Use<T>(this IState<T> state, Task whenReady, CancellationToken cancellationToken)
    {
        if (!whenReady.IsCompletedSuccessfully)
            await whenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await state.Use(cancellationToken).ConfigureAwait(false);
    }

    // This method runs w/o any update delay, so use it with caution for states with high invalidation frequency.
    public static async ValueTask<Computed<T>> WhenUnsafe<T>(
        this IState<T> state,
        Func<T, bool> predicate, CancellationToken cancellationToken = default)
    {
        var isMutable = state is IMutableState;
        while (true) {
            var computed = state.Computed;
            if (predicate.Invoke(computed.Value))
                return computed;

            await computed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            if (!isMutable) // Mutable state is invalidated on update, so no need to explicitly update it
                await computed.UpdateUntyped(cancellationToken).ConfigureAwait(false);
        }
    }
}

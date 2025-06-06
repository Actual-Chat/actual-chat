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
}

namespace ActualChat;

public static class StateExt
{
    public static Task<IComputed<T>> When<T>(this IState<T> state,
        Func<T, bool> predicate,
        CancellationToken cancellationToken = default)
        => state.Computed.When(predicate, cancellationToken);
}

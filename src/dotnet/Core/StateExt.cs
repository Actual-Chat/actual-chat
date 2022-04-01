namespace ActualChat;

public static class StateExt
{
    public static Task When<T>(this IState<T> state,
        Func<T, bool> predicate,
        CancellationToken cancellationToken = default)
        => state.Computed.When(predicate, cancellationToken);
}

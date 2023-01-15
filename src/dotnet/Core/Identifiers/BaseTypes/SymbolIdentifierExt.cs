namespace ActualChat;

public static class SymbolIdentifierExt
{
    public static TId Or<TId>(this TId value, TId noneReplacementValue)
        where TId : ISymbolIdentifier
        => !value.IsNone ? value : noneReplacementValue;

    public static TId Or<TId>(this TId value, Func<TId> noneReplacementFactory)
        where TId : ISymbolIdentifier
        => !value.IsNone ? value : noneReplacementFactory.Invoke();

    public static TId Or<TId, TState>(this TId value, TState state, Func<TState, TId> noneReplacementFactory)
        where TId : ISymbolIdentifier
        => !value.IsNone ? value : noneReplacementFactory.Invoke(state);
}

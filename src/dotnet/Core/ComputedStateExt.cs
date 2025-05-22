namespace ActualChat;

public static class ComputedStateExt
{
    public static bool IsSet<T>(this ComputedState<T?> state, [NotNullWhen(true)] out T? model)
        => !state.IsInitial(out model) && model is not null;
}

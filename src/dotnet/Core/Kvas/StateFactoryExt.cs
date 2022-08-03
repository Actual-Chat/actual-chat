namespace ActualChat.Kvas;

public static class StateFactoryExt
{
    public static IStoredState<T> NewStored<T, TScope>(
        this IStateFactory stateFactory,
        string key,
        MutableState<T>.Options? options = null)
    {
        options ??= DefaultStoredStateOptions<T>.Instance;
        return new StoredState<T, TScope>(options, key, stateFactory.Services);
    }

    public static IStoredState<T> NewStored<T>(
        this IStateFactory stateFactory,
        Type scope,
        string key,
        MutableState<T>.Options? options = null)
    {
        options ??= DefaultStoredStateOptions<T>.Instance;
        var kvas = (IKvas) stateFactory.Services.GetRequiredService(typeof(IKvas<>).MakeGenericType(scope));
        return new StoredState<T>(options, kvas, key, stateFactory.Services);
    }

    // Nested types

    private static class DefaultStoredStateOptions<T>
    {
        public static MutableState<T>.Options Instance { get; } = new();
    }
}

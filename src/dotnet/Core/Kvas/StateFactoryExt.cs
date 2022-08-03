namespace ActualChat.Kvas;

public static class StateFactoryExt
{
    public static IStoredState<T> NewStored<T>(
        this IStateFactory stateFactory,
        StoredState<T>.Options options)
        => new StoredState<T>(options, stateFactory.Services);

    public static IStoredState<T> NewStored<T, TScope>(
        this IStateFactory stateFactory,
        string key,
        Func<StoredState<T>.KvasOptions, StoredState<T>.KvasOptions>? configure = null)
    {
        var options = new StoredState<T>.KvasOptions(
            stateFactory.Services.GetRequiredService<IKvas<TScope>>(),
            key);
        if (configure != null)
            options = configure.Invoke(options);
        return new StoredState<T>(options, stateFactory.Services);
    }

    public static IStoredState<T> NewStored<T>(
        this IStateFactory stateFactory,
        Type scope,
        string key,
        Func<StoredState<T>.KvasOptions, StoredState<T>.KvasOptions>? configure = null)
    {
        var options = new StoredState<T>.KvasOptions(
            (IKvas) stateFactory.Services.GetRequiredService(typeof(IKvas<>).MakeGenericType(scope)),
            key);
        if (configure != null)
            options = configure.Invoke(options);
        return new StoredState<T>(options, stateFactory.Services);
    }
}

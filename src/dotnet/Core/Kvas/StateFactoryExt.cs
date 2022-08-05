namespace ActualChat.Kvas;

public static class StateFactoryExt
{
    // NewStored

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

    // NewSynced

    public static ISyncedState<T> NewSynced<T>(
        this IStateFactory stateFactory,
        SyncedState<T>.Options options)
        => new SyncedState<T>(options, stateFactory.Services);

    public static ISyncedState<T> NewSynced<T, TScope>(
        this IStateFactory stateFactory,
        string key,
        Func<SyncedState<T>.KvasOptions, SyncedState<T>.KvasOptions>? configure = null)
    {
        var options = new SyncedState<T>.KvasOptions(
            stateFactory.Services.GetRequiredService<IKvas<TScope>>(),
            key);
        if (configure != null)
            options = configure.Invoke(options);
        return new SyncedState<T>(options, stateFactory.Services);
    }

    public static ISyncedState<T> NewSynced<T>(
        this IStateFactory stateFactory,
        Type scope,
        string key,
        Func<SyncedState<T>.KvasOptions, SyncedState<T>.KvasOptions>? configure = null)
    {
        var options = new SyncedState<T>.KvasOptions(
            (IKvas) stateFactory.Services.GetRequiredService(typeof(IKvas<>).MakeGenericType(scope)),
            key);
        if (configure != null)
            options = configure.Invoke(options);
        return new SyncedState<T>(options, stateFactory.Services);
    }
}

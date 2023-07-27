namespace ActualChat.Kvas;

public static class StateFactoryExt
{
    public static readonly string ExternalOrigin = "-";

    // NewStored

    public static IStoredState<T> NewStored<T>(
        this IStateFactory stateFactory,
        StoredState<T>.Options options)
        => new StoredState<T>(options, stateFactory.Services);

    public static IStoredState<T> NewCustomStored<T>(
        this IStateFactory stateFactory,
        StoredState<T>.CustomOptions options)
        => new StoredState<T>(options, stateFactory.Services);

    public static IStoredState<T> NewKvasStored<T>(
        this IStateFactory stateFactory,
        StoredState<T>.KvasOptions options)
        => new StoredState<T>(options, stateFactory.Services);

    // NewSynced

    public static ISyncedState<T> NewSynced<T>(
        this IStateFactory stateFactory,
        SyncedState<T>.Options options)
        => new SyncedState<T>(options, stateFactory.Services);

    public static ISyncedState<T> NewCustomSynced<T>(
        this IStateFactory stateFactory,
        SyncedState<T>.CustomOptions options)
        => new SyncedState<T>(options, stateFactory.Services);

    public static ISyncedState<T> NewKvasSynced<T>(
        this IStateFactory stateFactory,
        SyncedState<T>.KvasOptions options)
        => new SyncedState<T>(options, stateFactory.Services);
}

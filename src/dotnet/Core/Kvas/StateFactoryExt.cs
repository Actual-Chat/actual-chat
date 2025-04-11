namespace ActualChat.Kvas;

public static class StateFactoryExt
{
    public static readonly string ExternalOrigin = "-";

    // NewStored

    public static StoredState<T> NewStored<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        this StateFactory stateFactory, StoredState<T>.Options options)
        => new(options, stateFactory.Services);

    public static StoredState<T> NewCustomStored<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        this StateFactory stateFactory, StoredState<T>.CustomOptions options)
        => new(options, stateFactory.Services);

    public static StoredState<T> NewKvasStored<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        this StateFactory stateFactory, StoredState<T>.KvasOptions options)
        => new(options, stateFactory.Services);

    // NewSynced

    public static SyncedState<T> NewSynced<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        this StateFactory stateFactory, SyncedState<T>.Options options)
        => new(options, stateFactory.Services);

    public static SyncedState<T> NewCustomSynced<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        this StateFactory stateFactory, SyncedState<T>.CustomOptions options)
        => new(options, stateFactory.Services);

    public static SyncedState<T> NewKvasSynced<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        this StateFactory stateFactory, SyncedState<T>.KvasOptions options)
        => new(options, stateFactory.Services);
}

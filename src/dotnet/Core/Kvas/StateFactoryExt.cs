using System.Diagnostics.CodeAnalysis;

namespace ActualChat.Kvas;

public static class StateFactoryExt
{
    public static readonly string ExternalOrigin = "-";

    // NewStored

    public static IStoredState<T> NewStored<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IStateFactory stateFactory, StoredState<T>.Options options)
        => new StoredState<T>(options, stateFactory.Services);

    public static IStoredState<T> NewCustomStored<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IStateFactory stateFactory, StoredState<T>.CustomOptions options)
        => new StoredState<T>(options, stateFactory.Services);

    public static IStoredState<T> NewKvasStored<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IStateFactory stateFactory, StoredState<T>.KvasOptions options)
        => new StoredState<T>(options, stateFactory.Services);

    // NewSynced

    public static ISyncedState<T> NewSynced<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IStateFactory stateFactory, SyncedState<T>.Options options)
        => new SyncedState<T>(options, stateFactory.Services);

    public static ISyncedState<T> NewCustomSynced<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IStateFactory stateFactory, SyncedState<T>.CustomOptions options)
        => new SyncedState<T>(options, stateFactory.Services);

    public static ISyncedState<T> NewKvasSynced<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IStateFactory stateFactory, SyncedState<T>.KvasOptions options)
        => new SyncedState<T>(options, stateFactory.Services);
}

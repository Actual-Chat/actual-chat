namespace ActualChat.Kvas;

public static class StateFactoryExt
{
    public static readonly string ForeignOrigin = "-";

    public static string GetOrigin(this IStateFactory stateFactory)
    {
        var originProvider = stateFactory.Services.GetRequiredService<IOriginProvider>();
        if (!originProvider.WhenReady.IsCompletedSuccessfully)
            throw StandardError.Internal("Origin provider isn't ready yet.");

        return originProvider.Origin;
    }

    public static async ValueTask<string> GetOriginAsync(this IStateFactory stateFactory, CancellationToken cancellationToken)
    {
        var originProvider = stateFactory.Services.GetRequiredService<IOriginProvider>();
        await originProvider.WhenReady.ConfigureAwait(false);
        return originProvider.Origin;
    }

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
        where T: IHasOrigin
        => new SyncedState<T>(options, stateFactory.Services);

    public static ISyncedState<T> NewCustomSynced<T>(
        this IStateFactory stateFactory,
        SyncedState<T>.CustomOptions options)
        where T: IHasOrigin
        => new SyncedState<T>(options, stateFactory.Services);

    public static ISyncedState<T> NewKvasSynced<T>(
        this IStateFactory stateFactory,
        SyncedState<T>.KvasOptions options)
        where T: IHasOrigin
        => new SyncedState<T>(options, stateFactory.Services);
}

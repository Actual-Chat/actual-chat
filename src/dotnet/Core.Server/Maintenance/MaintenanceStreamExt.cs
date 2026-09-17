namespace ActualChat;

public static class MaintenanceStreamExt
{
    public static async IAsyncEnumerable<T> RequireAvailable<T>(
        this IAsyncEnumerable<T> source,
        IMaintenancesBackend maintenances,
        ChatId chatId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var keys = chatId.ToMaintenanceKeyChain();
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            await maintenances.RequireAvailable(keys, cancellationToken).ConfigureAwait(false);
            yield return item;
        }
    }
}

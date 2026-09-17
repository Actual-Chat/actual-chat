namespace ActualChat;

public static class MaintenanceExt
{
    public static async Task<MaintenanceMode> Get(
        this IMaintenancesBackend maintenances,
        IEnumerable<MaintenanceKey> keys,
        CancellationToken cancellationToken)
    {
        var modes = await keys
            .Select(k => maintenances.Get(k, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        return modes.FirstOrDefault(m => m != MaintenanceMode.None);
    }

    // RequireAvailable

    public static async Task RequireAvailable(
        this IMaintenancesBackend maintenances,
        MaintenanceKey key,
        CancellationToken cancellationToken)
    {
        if (await maintenances.Get(key, cancellationToken).ConfigureAwait(false) != MaintenanceMode.None)
            throw StandardError.Constraint("The chat is in maintenance mode.");
    }

    public static async Task RequireAvailable(
        this IMaintenancesBackend maintenances,
        IEnumerable<MaintenanceKey> keys,
        CancellationToken cancellationToken)
    {
        if (await maintenances.Get(keys, cancellationToken).ConfigureAwait(false) != MaintenanceMode.None)
            throw StandardError.Constraint("The chat is in maintenance mode.");
    }

    // ChatId-related methods

    public static MaintenanceKey ToMaintenanceKey(this ChatId chatId)
        => new(
            ContentRef.Format(chatId),
            chatId.ShardKey.Head(MaintenanceKey.FullPartitionKeySize));

    // Returns the chat own key followed by the keys it inherits maintenance from
    public static MaintenanceKey[] ToMaintenanceKeyChain(this ChatId chatId)
    {
        var buffer = ArrayBuffer<MaintenanceKey>.Lease(true);
        try {
            while (true) {
                buffer.Add(chatId.ToMaintenanceKey());
                if (chatId is ThreadChatId threadChatId)
                    chatId = threadChatId.ParentChatId;
                else if (chatId is PlaceChatId { IsRoot: false } placeChatId)
                    chatId = placeChatId.PlaceId.RootChatId;
                else
                    return buffer.ToArray();
            }
        }
        finally {
            buffer.Release();
        }
    }

    public static Task<MaintenanceMode> Get(
        this IMaintenancesBackend maintenances,
        ChatId chatId,
        CancellationToken cancellationToken)
        => maintenances.Get(chatId.ToMaintenanceKeyChain(), cancellationToken);

    public static Task RequireAvailable(
        this IMaintenancesBackend maintenances,
        ChatId chatId,
        CancellationToken cancellationToken)
        => maintenances.RequireAvailable(chatId.ToMaintenanceKeyChain(), cancellationToken);
}

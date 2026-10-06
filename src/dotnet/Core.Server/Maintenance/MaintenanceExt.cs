namespace ActualChat;

public static class MaintenanceExt
{
    // RequireAvailable

    public static async ValueTask RequireAvailable(
        this IMaintenancesBackend maintenances,
        MaintenanceKey key,
        CancellationToken cancellationToken)
    {
        var maintenanceMode = await maintenances.GetMode(key, cancellationToken).ConfigureAwait(false);
        maintenanceMode.RequireNone("object");
    }

    // ChatId-related methods

    public static MaintenanceKey ToMaintenanceKey(this ChatId chatId)
    {
        // A Place keeps the maintenance of all its chats on its root key, see Maintenance.Targets
        if (chatId is PlaceChatId { IsRoot: false } placeChatId)
            chatId = placeChatId.PlaceId.RootChatId;
        return new(ContentRef.Format(chatId), chatId.ShardKey.Head(MaintenanceKey.FullPartitionKeySize));
    }

    public static string? ToMaintenanceTarget(this ChatId chatId)
        // The chat's entry in Maintenance.Targets of its key; null for a key the chat doesn't share
        => chatId is PlaceChatId ? ContentRef.Format(chatId) : null;

    public static async ValueTask<MaintenanceMode> GetMode(
        this IMaintenancesBackend maintenances,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        foreach (var scopeChatId in chatId.GetMaintenanceScope()) {
            var maintenance = await maintenances.Get(scopeChatId, cancellationToken).ConfigureAwait(false);
            if (maintenance.Mode != MaintenanceMode.None)
                return maintenance.Mode;
        }
        return MaintenanceMode.None;
    }

    public static async ValueTask RequireAvailable(
        this IMaintenancesBackend maintenances,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var maintenanceMode = await maintenances.GetMode(chatId, cancellationToken).ConfigureAwait(false);
        maintenanceMode.RequireNone("chat");
    }

    public static async Task WhenMaintenanceStarted(
        this IMaintenancesBackend maintenances,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var modeSource = new ComputedSource<MaintenanceMode>(
            maintenances.GetServices(),
            (_, ct) => maintenances.GetMode(chatId, ct).AsTask());
        // A failed read doesn't count: the next invalidation retries it
        await modeSource.Computed
            .When((mode, error) => error is null && mode != MaintenanceMode.None, cancellationToken)
            .ConfigureAwait(false);
    }

    // Import-specific methods

    public static async ValueTask<Maintenance?> GetImport(
        this IMaintenancesBackend maintenances,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        // The maintenance that makes chatId part of an import, own or inherited; OwnerId is its ChatImportId
        foreach (var scopeChatId in chatId.GetMaintenanceScope()) {
            var maintenance = await maintenances.Get(scopeChatId, cancellationToken).ConfigureAwait(false);
            if (maintenance.Mode == MaintenanceMode.Import)
                return maintenance;
        }
        return null;
    }

    public static async ValueTask<bool> IsImporting(
        this IMaintenancesBackend maintenances,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var import = await maintenances.GetImport(chatId, cancellationToken).ConfigureAwait(false);
        return import is not null;
    }

    public static async ValueTask RequireNotImporting(
        this IMaintenancesBackend maintenances,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        // Unlike RequireAvailable, this lets System maintenance through: it keeps ordinary chat
        // activity out of an import session rather than gating trusted backend operations.
        if (await maintenances.IsImporting(chatId, cancellationToken).ConfigureAwait(false))
            throw StandardError.Constraint("The chat is in import mode.");
    }

    // Private methods

    private static ChatId[] GetMaintenanceScope(this ChatId chatId)
        // The chat itself plus the chat a thread inherits maintenance from
        => chatId is ThreadChatId threadChatId
            ? [chatId, threadChatId.ParentChatId]
            : [chatId];

    private static async ValueTask<Maintenance> Get(
        this IMaintenancesBackend maintenances,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        // A chat with a key of its own reads GetMode first, so only one under maintenance reaches Get;
        // a Place chat always reads its root's row, which holds the targets.
        var key = chatId.ToMaintenanceKey();
        var target = chatId.ToMaintenanceTarget();
        if (target is null) {
            var maintenanceMode = await maintenances.GetMode(key, cancellationToken).ConfigureAwait(false);
            if (maintenanceMode == MaintenanceMode.None)
                return Maintenance.None;
        }

        var maintenance = await maintenances.Get(key, cancellationToken).ConfigureAwait(false);
        return maintenance.GetMode(target) == MaintenanceMode.None
            ? Maintenance.None
            : maintenance;
    }
}

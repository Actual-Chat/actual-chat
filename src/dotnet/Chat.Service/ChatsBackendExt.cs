namespace ActualChat.Chat;

public static class ChatsBackendExt
{
    public static async ValueTask<ChatEntry?> GetEntry(
        this IChatsBackend chatsBackend,
        ChatEntryId entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId.IsNone)
            return null;

        var idTile = Constants.Chat.IdTileStack.FirstLayer.GetTile(entryId.LocalId);
        var tile = await chatsBackend.GetTile(entryId.ChatId,
                entryId.Kind,
                idTile.Range,
                false,
                cancellationToken)
            .ConfigureAwait(false);
        return tile.Entries.SingleOrDefault(e => e.LocalId == entryId.LocalId);
    }

    public static async ValueTask<ChatEntry?> GetRemovedEntry(
        this IChatsBackend chatsBackend,
        ChatEntryId entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId.IsNone)
            return null;

        var idTile = Constants.Chat.IdTileStack.FirstLayer.GetTile(entryId.LocalId);
        var tile = await chatsBackend.GetTile(entryId.ChatId,
                entryId.Kind,
                idTile.Range,
                true,
                cancellationToken)
            .ConfigureAwait(false);
        return tile.Entries.SingleOrDefault(e => e.LocalId == entryId.LocalId);
    }
}

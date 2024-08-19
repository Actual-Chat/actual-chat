namespace ActualChat.Chat;

public static class ChatsExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ChatEntryReader NewEntryReader(
        this IChats chats,
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind)
        => new(chats, session, chatId, entryKind);

    public static async ValueTask<ChatEntry?> GetEntry(
        this IChats chats,
        Session session,
        ChatEntryId entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId.IsNone)
            return null;

        try {
            var idTile = Constants.Chat.ServerIdTileStack.FirstLayer.GetTile(entryId.LocalId);
            var tile = await chats.GetTile(session,
                    entryId.ChatId,
                    entryId.Kind,
                    idTile.Range,
                    cancellationToken)
                .ConfigureAwait(false);
            var entry = tile.Entries.SingleOrDefault(e => e.LocalId == entryId.LocalId);
            return entry;
        }
        catch (NotFoundException) {
            return null;
        }
    }
}

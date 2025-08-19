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

    public static async IAsyncEnumerable<ChatEntry> ReadReverse(
        this IChats chats,
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var idRange = await chats.GetIdRange(session, chatId, entryKind, cancellationToken).ConfigureAwait(false);
        var entryReader = chats.NewEntryReader(session, chatId, entryKind);
        await foreach (var chatEntry in entryReader.ReadReverse(idRange, cancellationToken).ConfigureAwait(false))
            yield return chatEntry;
    }
}

namespace ActualChat.Chat.UI.Blazor.Services;

public static class ChatsExt
{
    public static ChatEntryReader NewEntryReader(
        this IChats chats,
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind,
        TileLayer<long>? idTileLayer = null)
        => new(chats, session, chatId, entryKind, idTileLayer);

    public static ValueTask<ChatEntry?> Get(
        this IChats chats,
        Session session,
        ChatEntryId entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId.IsNone)
            return ValueTask.FromResult((ChatEntry?)null);

        var reader = chats.NewEntryReader(session, entryId.ChatId, entryId.Kind);
        return reader.Get(entryId.LocalId, cancellationToken);
    }
}

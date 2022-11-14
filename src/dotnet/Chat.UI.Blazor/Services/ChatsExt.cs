namespace ActualChat.Chat.UI.Blazor.Services;

public static class ChatsExt
{
    public static ChatEntryReader NewEntryReader(
        this IChats chats,
        Session session,
        string chatId,
        ChatEntryKind entryKind,
        TileLayer<long>? idTileLayer = null)
        => new(chats, session, chatId, entryKind, idTileLayer);
}

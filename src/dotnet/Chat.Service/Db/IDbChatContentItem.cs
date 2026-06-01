namespace ActualChat.Chat.Db;

// Common surface area for chat content tables that are queried by (ChatId, At, EntryLocalId, LocalIndex).
// Lets ChatsBackend share period aggregation + paged page-load LINQ across
// DbChatVisualMediaItem / DbChatFileItem / DbChatLinkItem.
public interface IDbChatContentItem
{
    string ChatId { get; }
    DateTime At { get; }
    long EntryLocalId { get; }
    int LocalIndex { get; }
}

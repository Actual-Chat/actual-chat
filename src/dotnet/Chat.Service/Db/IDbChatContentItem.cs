namespace ActualChat.Chat.Db;

// Common surface area for chat content tables. Lets ChatsBackend share the
// period aggregation, paged page-load LINQ, and the update-by-EntryId command
// across DbChatVisualMediaItem / DbChatFileItem / DbChatLinkItem.
public interface IDbChatContentItem
{
    string ChatId { get; }
    string EntryId { get; }
    DateTime At { get; }
    long EntryLocalId { get; }
    int LocalIndex { get; }
}

namespace ActualChat.Chat.Db;

// Common surface area for chat content tables that are queried by (ChatId, At) —
// lets ChatsBackend.GetContentPeriods share the per-month aggregation query
// across DbChatVisualMediaItem / DbChatFileItem / DbChatLinkItem.
public interface IDbChatContentItem
{
    string ChatId { get; }
    DateTime At { get; }
}

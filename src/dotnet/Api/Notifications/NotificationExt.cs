namespace ActualChat.Notifications;

public static class NotificationExt
{
    // The device push tag groups a chat's notifications under one entry (one banner per chat).
    // Returns null for non-chat notifications. Shared by the FCM send path and the client-side
    // reconciler so both derive the same tag.
    public static string? GetChatTag(this Notification notification)
        => notification switch {
            ConversationNotification n => n.ChatId.Value,
            ChatEntryRelatedNotification n => n.ChatId.Value,
            ChatEntryNotification n => n.ChatId.Value,
            ChatNotification n when ChatId.TryParse(n.SimilarityKey, out var chatId) => chatId.Value,
            _ => null,
        };

    // The in-app deep link a notification points at (entry if it has one, else the chat).
    // Mirrors the link the FCM send path builds; used by the client reconciler to create a
    // missing notification with a working tap target.
    public static LocalUrl GetChatLink(this Notification notification)
    {
        var entryId = notification switch {
            ConversationNotification n => (ChatEntryId?)ChatEntryId.New(n.ChatId, n.StartEntryLid),
            ChatEntryRelatedNotification n when n.EntryLid > 0 => (ChatEntryId?)n.EntryId,
            ChatEntryNotification n => n.EntryId,
            _ => null,
        };
        if (entryId is { } e)
            return Links.Chat(e);
        return ChatId.TryParse(notification.GetChatTag() ?? "", out var chatId)
            ? Links.Chat(chatId)
            : Links.Chats;
    }
}

namespace ActualChat.Notifications;

public static class NotificationExt
{
    // The device push tag: the OS replaces a shown banner by tag, so the tag must map 1:1 to the
    // server-side notification identity or dismissal-by-tag closes the wrong banners.
    // Individually-seen kinds (mention, attention, reaction) tag by their entry — each keeps its
    // own banner; chat-coalescing kinds share one banner per chat. Shared by the FCM send path,
    // the dismissal path and the client-side reconciler so all derive the same tag.
    public static string? GetPushTag(this Notification notification)
        => notification switch {
            ChatEntryNotification n => n.EntryId.Value,
            // A call's ring and its dismissal must collapse onto a banner of their own —
            // the chat-wide tag would make a call dismissal close the chat's message banners too.
            CallNotification n => Constants.Notification.CallTagPrefix + n.ChatId.Value,
            _ => notification.GetChatTag(),
        };

    // The chat a notification belongs to, as a tag (one value per chat). Returns null for
    // non-chat notifications.
    public static string? GetChatTag(this Notification notification)
        => notification switch {
            ConversationNotification n => n.ChatId.Value,
            ChatEntryRelatedNotification n => n.ChatId.Value,
            ChatEntryNotification n => n.ChatId.Value,
            CallNotification n => n.ChatId.Value,
            ChatNotification n when ChatId.TryParse(n.SimilarityKey, out var chatId) => chatId.Value,
            _ => null,
        };

    public static ChatId? GetChatId(this Notification notification)
        => ChatId.TryParse(notification.GetChatTag() ?? "", out var chatId) ? chatId : null;

    // The in-app deep link a notification points at (entry if it has one, else the chat).
    // Mirrors the link the FCM send path builds; used by the client reconciler to create a
    // missing notification with a working tap target.
    public static LocalUrl GetChatLink(this Notification notification)
    {
        var entryId = notification switch {
            ConversationNotification n => (ChatEntryId?)ChatEntryId.New(n.ChatId, n.StartEntryLid),
            ChatEntryRelatedNotification n when n.EntryLid > 0 => (ChatEntryId?)n.StartEntryId,
            ChatEntryNotification n => n.EntryId,
            _ => null,
        };
        if (entryId is { } e)
            return Links.Chat(e);

        return notification.GetChatId() is { } chatId ? Links.Chat(chatId) : Links.Chats;
    }
}

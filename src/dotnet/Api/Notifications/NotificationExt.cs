namespace ActualChat.Notifications;

public static class NotificationExt
{
    public static string? GetPushTag(this Notification notification)
        // The OS replaces a shown banner by tag, so this must map 1:1 to the server-side identity
        // or dismissal-by-tag closes the wrong banners. Individually-seen kinds (mention,
        // attention, reaction) tag by entry and keep their own banner; chat-coalescing kinds share
        // one per chat. The send, dismissal and reconcile paths all derive the tag from here.
        => notification switch {
            ChatEntryNotification n => n.EntryId.Value,
            // A call's ring and its dismissal must collapse onto a banner of their own —
            // the chat-wide tag would make a call dismissal close the chat's message banners too.
            CallNotification n => Constants.Notification.CallTagPrefix + n.ChatId.Value,
            _ => notification.GetChatTag(),
        };

    public static NotificationDismissMode GetDismissMode(NotificationKind kind)
        // What Notification.DismissMode says, for a caller holding only a kind - a push payload on
        // a client. NotificationDismissModeTest asserts the two agree for every kind.
        => kind switch {
            NotificationKind.Message or NotificationKind.Reply or NotificationKind.Thread
                or NotificationKind.Mention or NotificationKind.Attention
                or NotificationKind.Conversation => NotificationDismissMode.OnRead,
            NotificationKind.Reaction => NotificationDismissMode.OnView,
            _ => NotificationDismissMode.Explicit,
        };

    public static string? GetChatTag(this Notification notification)
        // The chat a notification belongs to, one value per chat; null for non-chat kinds.
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

    public static LocalUrl GetChatLink(this Notification notification)
    {
        // Mirrors the link the FCM send path builds, so a notification the client reconciler
        // re-creates gets the same tap target: its entry if it has one, else the chat.
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

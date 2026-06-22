namespace ActualChat.Notifications;

public static class NotificationExt
{
    // The device push tag groups a chat's notifications under one entry (one banner per chat).
    // Returns null for non-chat notifications. Shared by the FCM send path and the client-side
    // reconciler so both derive the same tag.
    public static string? GetChatTag(this Notification notification)
        => notification switch {
            ChatEntryRelatedNotification n => n.ChatId.Value,
            ChatEntryNotification n => n.ChatId.Value,
            ChatNotification n when ChatId.TryParse(n.SimilarityKey, out var chatId) => chatId.Value,
            _ => null,
        };
}

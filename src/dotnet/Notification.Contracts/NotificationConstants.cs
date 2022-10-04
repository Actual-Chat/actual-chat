namespace ActualChat.Notification;

public static class NotificationConstants
{
    public static class ChannelIds
    {
        // TODO: create more channels and groups
        // to provide to user more fine-grained control over notifications.
        public const string Default = "fcm_default_channel";
    }

    public static class MessageDataKeys
    {
        public const string ChatId = "chatId";
        public const string EntryId = "entryId";
        public const string Icon = "icon";
        public const string Link = "link";
        public const string NotificationId = "notificationId";
        public const string Tag = "tag";

        public static readonly string[] ValidKeys = {
            ChatId, EntryId, Icon, Link, NotificationId, Tag
        };

        public static bool IsValidKey(string key)
            => ValidKeys.Contains(key, StringComparer.Ordinal);
    }
}

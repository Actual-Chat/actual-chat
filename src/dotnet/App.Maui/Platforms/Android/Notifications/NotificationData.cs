namespace ActualChat.App.Maui;

public class NotificationData(string messageId, Dictionary<string, string> data)
{
    private static ILogger? _log;

    private static ILogger Log => _log ??= StaticLog.For<NotificationData>();

    public string MessageId { get; } = messageId;

    public NotificationKind NotificationKind {
        get {
            data.TryGetValue(Constants.Notification.MessageDataKeys.Kind, out var sKind);
            if (!Enum.TryParse<NotificationKind>(sKind, true, out var kind))
                kind = NotificationKind.Invalid;
            return kind;
        }
    }

    public ChatId? ChatId {
        get {
            data.TryGetValue(Constants.Notification.MessageDataKeys.ChatId, out var chatSid);
            var chatId = ChatId.TryParse(chatSid, allowNull: true);
            if (chatId is null && !chatSid.IsNullOrEmpty())
                Log.LogWarning("Invalid ChatId: '{ChatId}'", chatSid);
            return chatId;
        }
    }

    public long LastEntryLocalId {
        get {
            data.TryGetValue(Constants.Notification.MessageDataKeys.LastEntryLocalId, out var sLastEntryLocalId);
            if (!long.TryParse(sLastEntryLocalId, out var lastEntryLocalId)) {
                Log.LogWarning("Invalid LastEntryLocalId: '{LastEntryLocalId}'", sLastEntryLocalId);
                lastEntryLocalId = 0;
            }
            return lastEntryLocalId;
        }
    }

    public string? Title
        => data.GetValueOrDefault(Constants.Notification.MessageDataKeys.Title, "").NullIfEmpty();

    public string? Body
        => data.GetValueOrDefault(Constants.Notification.MessageDataKeys.Body, "").NullIfEmpty();

    public string? ImageUrl
        => data.GetValueOrDefault(Constants.Notification.MessageDataKeys.ImageUrl, "").NullIfEmpty();

    public string? Link
        => data.GetValueOrDefault(Constants.Notification.MessageDataKeys.Link, "").NullIfEmpty();

    public string? Tag
        => data.GetValueOrDefault(Constants.Notification.MessageDataKeys.Tag, "").NullIfEmpty();

    // Structured transcript lines for MessagingStyle rendering; empty when the push predates them.
    public IReadOnlyList<PushMessage> Messages
        => PushMessage.FromJson(data.GetValueOrDefault(Constants.Notification.MessageDataKeys.Messages));

    // A silent update refreshes the banner content without alerting (sound/vibration).
    public bool Silent
        => bool.TryParse(data.GetValueOrDefault(Constants.Notification.MessageDataKeys.Silent), out var silent) && silent;

    // The local id of the entry this notification points at (for the read-position skip).
    public long EntryLocalId {
        get {
            data.TryGetValue(Constants.Notification.MessageDataKeys.ChatEntryId, out var sEntryId);
            return ChatEntryId.TryParse(sEntryId, out var entryId) ? entryId.LocalId : LastEntryLocalId;
        }
    }

    public IReadOnlyList<string> DismissedTags
        => data.GetValueOrDefault(Constants.Notification.MessageDataKeys.DismissedTags, "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

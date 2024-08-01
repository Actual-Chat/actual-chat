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

    public ChatId ChatId {
        get {
            data.TryGetValue(Constants.Notification.MessageDataKeys.ChatId, out var sChatId);
            var chatId = new ChatId(sChatId, ParseOrNone.Option);
            if (chatId.IsNone && !sChatId.IsNullOrEmpty())
                Log.LogWarning("Invalid ChatId: '{ChatId}'", sChatId);
            return chatId;
        }
    }

    public long LastEntryLocalId {
        get {
            data.TryGetValue(Constants.Notification.MessageDataKeys.LastEntryLocalId, out var sLastEntryLocalId);
            if (!long.TryParse(sLastEntryLocalId, CultureInfo.InvariantCulture, out var lastEntryLocalId)) {
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
}

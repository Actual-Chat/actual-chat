namespace ActualChat.App.Maui;

public class NotificationData
{
    private static ILogger? _log;
    private readonly Dictionary<string, string> _data;

    private static ILogger Log => _log ??= StaticLog.For<NotificationData>();

    public string MessageId { get; }

    public NotificationKind NotificationKind {
        get {
            _data.TryGetValue(Constants.Notification.MessageDataKeys.Kind, out var sKind);
            if (!Enum.TryParse<NotificationKind>(sKind, true, out var kind))
                kind = NotificationKind.Invalid;
            return kind;
        }
    }

    public ChatId ChatId {
        get {
            _data.TryGetValue(Constants.Notification.MessageDataKeys.ChatId, out var sChatId);
            var chatId = new ChatId(sChatId, ParseOrNone.Option);
            if (chatId.IsNone && !sChatId.IsNullOrEmpty())
                Log.LogWarning("Invalid ChatId: '{ChatId}'", sChatId);
            return chatId;
        }
    }

    public long LastEntryLocalId {
        get {
            _data.TryGetValue(Constants.Notification.MessageDataKeys.LastEntryLocalId, out var sLastEntryLocalId);
            if (!long.TryParse(sLastEntryLocalId, CultureInfo.InvariantCulture, out var lastEntryLocalId)) {
                Log.LogWarning("Invalid LastEntryLocalId: '{LastEntryLocalId}'", sLastEntryLocalId);
                lastEntryLocalId = 0;
            }
            return lastEntryLocalId;
        }
    }

    public string? Title {
        get {
            _data.TryGetValue(Constants.Notification.MessageDataKeys.Title, out var title);
            return title;
        }
    }

    public string? Body {
        get {
            _data.TryGetValue(Constants.Notification.MessageDataKeys.Body, out var body);
            return body;
        }
    }

    public string? ImageUrl {
        get {
            _data.TryGetValue(Constants.Notification.MessageDataKeys.ImageUrl, out var imageUrl);
            return imageUrl;
        }
    }

    public string? Link {
        get {
            _data.TryGetValue(Constants.Notification.MessageDataKeys.Link, out var link);
            return link;
        }
    }

    public string? Tag {
        get {
            _data.TryGetValue(Constants.Notification.MessageDataKeys.Tag, out var tag);
            return tag;
        }
    }

    public NotificationData(string messageId, Dictionary<string, string> data)
    {
        MessageId = messageId;
        _data = data;
    }
}

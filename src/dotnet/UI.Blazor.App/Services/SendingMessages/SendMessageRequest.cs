namespace ActualChat.UI.Blazor.App.Services;

public sealed record AfterSendMessageHandler(string Key, string Args);

public sealed class SendMessageRequest
{
    public required ChatId ChatId { get; init;  }
    public required string Text { get; init; }
    public long? LocalId { get; private set;  }
    public Option<long?> RepliedEntryLid { get; private set; }
    public string? QuotedText { get; private set; }
    public FilesUploadHandle? Uploads { get; private set; }
    public IReadOnlyList<MediaRef> ExistingMedia { get; private set; } = [];
    public AfterSendMessageHandler? AfterSendMessageHandler { get; private set; }
    public GeoPoint? LocationPoint { get; private set; }
    public bool IsLocationPlace { get; private set; }
    public SharedLocationId? LocationId { get; private set; }

    public static SendMessageRequest NewMessage(
        ChatId chatId,
        string text,
        FilesUploadHandle? uploads = null,
        AfterSendMessageHandler? afterSendMessageHandler = null,
        IReadOnlyList<MediaRef>? existingMedia = null)
        => new () {
            ChatId = chatId,
            Text = text,
            Uploads = uploads,
            ExistingMedia = existingMedia ?? [],
            AfterSendMessageHandler = afterSendMessageHandler,
        };

    public static SendMessageRequest NewLocation(ChatId chatId, GeoPoint point, bool isPlace)
        => new () {
            ChatId = chatId,
            Text = "",
            LocationPoint = point,
            IsLocationPlace = isPlace,
        };

    // For a location that already exists, such as a live share the reporter has just created
    public static SendMessageRequest NewLocation(ChatId chatId, SharedLocationId locationId)
        => new () {
            ChatId = chatId,
            Text = "",
            LocationId = locationId,
        };

    public static SendMessageRequest EditMessage(ChatEntryId chatEntryId, string newText)
        => new () {
            ChatId = chatEntryId.ChatId,
            LocalId = chatEntryId.LocalId,
            Text = newText,
        };

    public static SendMessageRequest ReplyMessage(
        ChatId chatId,
        ChatEntryId relatedMessageId,
        string text,
        FilesUploadHandle? uploads = null,
        string? quotedText = null)
    {
        if (relatedMessageId.ChatId != chatId)
            throw new ArgumentException("Related message must be in the same chat", nameof(relatedMessageId));

        return new SendMessageRequest {
            ChatId = chatId,
            RepliedEntryLid = relatedMessageId.LocalId,
            QuotedText = quotedText,
            Text = text,
            Uploads = uploads,
        };
    }
}

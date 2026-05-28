namespace ActualChat.UI.Blazor.App.Services;

public sealed record AfterSendMessageHandler(string Key, string Args);

public sealed class SendMessageRequest
{
    public required ChatId ChatId { get; init;  }
    public required string Text { get; init; }
    public long? LocalId { get; private set;  }
    public Option<long?> RepliedEntryLid { get; private set; }
    public FilesUploadHandle? Uploads { get; private set; }
    public IReadOnlyList<MediaRef> ExistingMedia { get; private set; } = [];
    public AfterSendMessageHandler? AfterSendMessageHandler { get; private set; }

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

    public static SendMessageRequest EditMessage(ChatEntryId chatEntryId, string newText)
        => new () {
            ChatId = chatEntryId.ChatId,
            LocalId = chatEntryId.LocalId,
            Text = newText,
        };

    public static SendMessageRequest ReplyMessage(ChatId chatId, ChatEntryId relatedMessageId, string text, FilesUploadHandle? uploads = null)
    {
        if (relatedMessageId.ChatId != chatId)
            throw new ArgumentException("Related message must be in the same chat", nameof(relatedMessageId));

        return new SendMessageRequest {
            ChatId = chatId,
            RepliedEntryLid = relatedMessageId.LocalId,
            Text = text,
            Uploads = uploads,
        };
    }
}

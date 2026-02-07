namespace ActualChat.UI.Blazor.App.Services;

public record SendFilesRequest(IAttachmentList Attachments);

public record UploadsHandle : IDisposable
{
    public int Count { get; init; }

    public void Dispose()
    {

    }
}

public sealed record AfterSendMessageHandler(string Key, string Args);

public sealed class SendMessageRequest
{
    public required ChatId ChatId { get; init;  }
    public required string Text { get; init; }
    public long? LocalId { get; private set;  }
    public Option<long?> RepliedEntryLid { get; private set; }
    public UploadsHandle? Uploads { get; private set; }
    public AfterSendMessageHandler? AfterSendMessageHandler { get; private set; }

    public static SendMessageRequest NewMessage(ChatId chatId, string text, UploadsHandle? uploads = null, AfterSendMessageHandler? afterSendMessageHandler = null)
        => new () {
            ChatId = chatId,
            Text = text,
            Uploads = uploads,
            AfterSendMessageHandler = afterSendMessageHandler,
        };

    public static SendMessageRequest EditMessage(TextEntryId textEntryId, string newText)
        => new () {
            ChatId = textEntryId.ChatId,
            LocalId = textEntryId.LocalId,
            Text = newText,
        };

    public static SendMessageRequest ReplyMessage(ChatId chatId, TextEntryId relatedMessageId, string text)
    {
        if (relatedMessageId.ChatId != chatId)
            throw new ArgumentException("Related message must be in the same chat", nameof(relatedMessageId));

        return new SendMessageRequest {
            ChatId = chatId,
            RepliedEntryLid = relatedMessageId.LocalId,
            Text = text,
        };
    }
}

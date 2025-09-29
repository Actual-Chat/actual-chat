namespace ActualChat.UI.Blazor.App.Services;

public sealed record SendMessageRequest(ChatId ChatId, long? LocalId, string Text)
{
    public Option<long?> RepliedEntryLid { get; init; }
    public IAttachmentList? Attachments { get; init; }
}

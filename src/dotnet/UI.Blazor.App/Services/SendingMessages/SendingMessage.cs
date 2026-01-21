using ActualChat.Hashing;

namespace ActualChat.UI.Blazor.App.Services;

public record SendingMessage(
    string Uuid,
    ChatId ChatId,
    long? LocalId,
    Moment BeginsAt,
    string Content,
    HashString ContentHash,
    AttachmentUploads? AttachmentUploads,
    Action CancelSendRequested)
{
    public ChatEntry? PostedChatEntry { get; private set; }
    public Exception? Error { get; private set; }
    public Moment? SentMoment { get; private set; }
    public Moment? CompleteMoment { get; private set; }
    public bool IsCompleted { get; private set; }
    public bool ToBeRemoved { get; private set; }
    public bool LoadedForDisplay { get; private set; }

    public void Posted(ChatEntry chatEntry, Moment now)
    {
        PostedChatEntry = chatEntry;
        SentMoment = now;
    }

    public void Complete(Moment now)
    {
        IsCompleted = true;
        CompleteMoment = now;
    }

    public void Complete(Exception error)
    {
        Error = error;
        IsCompleted = true;
    }

    public void Cancel()
    {
        if (IsCompleted)
            return;

        CancelSendRequested();
    }

    public void MarkToRemove()
        => ToBeRemoved = true;

    public void MarkLoadedForDisplay()
        => LoadedForDisplay = true;
}

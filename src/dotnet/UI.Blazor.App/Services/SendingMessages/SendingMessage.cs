using ActualChat.Hashing;

namespace ActualChat.UI.Blazor.App.Services;

public record SendingMessage(
    ChatId ChatId,
    long? LocalId,
    Moment BeginsAt,
    string Content,
    HashString ContentHash,
    string Uuid,
    AttachmentUploads? AttachmentUploads,
    CancellationTokenSource CancellationTokenSource) : IDisposable
{
    private bool _isDisposed;

    public ChatEntry? PostedChatEntry { get; private set; }
    public Exception? Error { get; private set; }
    public Moment? SentMoment { get; private set; }
    public bool IsCompleted { get; private set; }
    public bool ToBeRemoved { get; private set; }

    public void Complete(ChatEntry chatEntry, Moment now)
    {
        PostedChatEntry = chatEntry;
        SentMoment = now;
        IsCompleted = true;
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

        CancellationTokenSource.Cancel();
    }

    public void MarkToRemove()
        => ToBeRemoved = true;

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        CancellationTokenSource.DisposeSilently();
    }
}

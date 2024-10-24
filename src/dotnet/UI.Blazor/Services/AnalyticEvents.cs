namespace ActualChat.UI.Blazor.Services;

public class AnalyticEvents
{
    public event EventHandler<MessagePostedEventArgs>? MessagedPosted;
    public event EventHandler? RecordingStarted;
    public event EventHandler<RecordingCompletedEventArgs>? RecordingCompleted;
    public event EventHandler<ModalStateChangedEventArgs>? ModalStateChanged;

    public void RaiseMessagePosted(bool isReply, bool hasText, int attachmentCount)
        => MessagedPosted?.Invoke(this, new MessagePostedEventArgs(isReply, hasText, attachmentCount));

    public void RaiseRecordingStarted()
        => RecordingStarted?.Invoke(this, EventArgs.Empty);

    public void RaiseRecordingCompleted(int durationInMs)
        => RecordingCompleted?.Invoke(this, new RecordingCompletedEventArgs(durationInMs));

    public void RaiseModalStateChanged(string modalName, bool isOpen)
        => ModalStateChanged?.Invoke(this, new ModalStateChangedEventArgs(modalName, isOpen));

    public class MessagePostedEventArgs(bool isReply, bool hasText, int attachmentCount) : EventArgs
    {
        public bool IsReply { get; } = isReply;
        public bool HasText { get; } = hasText;
        public int AttachmentCount { get; } = attachmentCount;
    }

    public class RecordingCompletedEventArgs(int durationInMs) : EventArgs
    {
        public int DurationInMs { get; } = durationInMs;
    }

    public class ModalStateChangedEventArgs(string modalName, bool isOpen) : EventArgs
    {
        public string ModalName { get; } = modalName;
        public bool IsOpen { get; } = isOpen;
    }
}

namespace ActualChat.Live;

public enum TypingKind
{
    Typing = 0,
    // Reserved: emitted by the attachment-upload flow later, so the same channel can show
    // "sending files…" without a contract change.
    SendingFiles = 1,
}

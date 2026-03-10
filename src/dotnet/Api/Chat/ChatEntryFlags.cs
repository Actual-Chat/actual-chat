namespace ActualChat.Chat;

[Flags]
public enum ChatEntryFlags
{
    None = 0,
    IsRemoved = 1 << 0,
    HasReactions = 1 << 1,
    IsThreadStart = 1 << 2,
    IsThread = 1 << 3,
    HasUploadingAttachments = 1 << 4,
}

namespace ActualChat.Chat.UI.Blazor.Components;

[Flags]
public enum ChatMessageFlags
{
    Unread = 1,
    BlockStart = 1 << 1,
    ForwardStart = 1 << 2,
    HasEntryKindSign = 1 << 3,
    ForwardAuthorStart = 1 << 4,
    UnreadBlockStart = 1 << 5,
}

namespace ActualChat.UI.Blazor.App.Components;

[Flags]
public enum ChatMessageFlags
{
    Unread = 1,
    BlockStart = 1 << 1,
    ForwardStart = 1 << 2,
    HasEntryKindSign = 1 << 3,
    ForwardAuthorStart = 1 << 4,
}

namespace ActualChat.UI.Blazor.App.Components;

[Flags]
public enum ChatMessageFlags
{
    Unread = 1,
    BlockStart = 1 << 1,
    ForwardStart = 1 << 2,
    // What the kind icon shows: audio that has landed, audio still streaming, or the placeholder
    // standing in for one. Not entry.HasAudio, which is only the first of those.
    Audio = 1 << 3,
    ForwardAuthorStart = 1 << 4,
    IsOwnMessage = 1 << 5,
    FirstInConversation = 1 << 6,
}

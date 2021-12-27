using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

public record ChatMessageModel(
    ChatEntry Entry,
    Author Author)
{
    public bool IsBlockStart { get; init; }
    public bool IsBlockEnd { get; init; }
}

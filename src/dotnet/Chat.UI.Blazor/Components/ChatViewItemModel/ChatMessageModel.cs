using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

public record ChatMessageModel(
    string Id,
    ChatEntry Entry,
    Author Author) : IChatViewItemModel
{
    public bool IsBlockStart { get; init; }
    public bool IsBlockEnd { get; init; }
}

namespace ActualChat.Chat.UI.Blazor.Services;

public record ChatListItemModel(ChatState ChatState, bool IsLastItemInBlock, bool IsFirstItem) : IVirtualListItem
{
    public Symbol Key => ChatState.Id;
    public int CountAs => 1;
}

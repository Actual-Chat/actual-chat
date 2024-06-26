namespace ActualChat.Chat.UI.Blazor.Services;

public record ChatListItemModel(int Position, ChatState ChatState, bool IsLastItemInBlock, bool IsFirstItem) : IVirtualListItem
{
    public Symbol Key => Position.ToString();
    public int CountAs => 1;
}

namespace ActualChat.Chat.UI.Blazor.Services;

public record ChatListItemModel(int Position, ChatInfo ChatInfo, bool IsLastItemInBlock, bool IsFirstItem) : IVirtualListItem
{
    public Symbol Key => Position.ToString();
    public int CountAs => 1;
}

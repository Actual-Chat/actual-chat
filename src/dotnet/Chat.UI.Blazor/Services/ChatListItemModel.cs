namespace ActualChat.Chat.UI.Blazor.Services;

public record ChatListItemModel(int Position, ChatInfo ChatInfo, bool IsLastItemInBlock, bool IsFirstItem) : IVirtualListItem
{
    public string Key { get; } = Position.ToString(CultureInfo.InvariantCulture);
    public string RenderKey => ChatInfo.Id.Value;
    public int CountAs => 1;
}

namespace ActualChat.Chat.UI.Blazor.Services;

public record ChatListItemModel(int Position, Chat Chat, bool IsLastItemInBlock, bool IsFirstItem) : IVirtualListItem
{
    public string Key { get; } = Position.ToString(CultureInfo.InvariantCulture);
    public string RenderKey => Chat.Id.Value;
    public int CountAs => 1;
}

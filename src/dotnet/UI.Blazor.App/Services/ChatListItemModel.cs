namespace ActualChat.UI.Blazor.App.Services;

public record ChatListItemModel(int Position, Chat.Chat Chat, bool IsLastItemInBlock, bool IsFirstItem) : IVirtualListItem
{
    public string Key { get; } = Position.ToString();
    public string RenderKey => Chat.Id.Value;
    public bool IsGroup => false;
    public bool ShouldSkipKey => false;
    public bool HasRegularSize => !IsLastItemInBlock;
}

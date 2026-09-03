namespace ActualChat.UI.Blazor.App.Services;

// Chat is null for the invite-friends banner row that closes a short list
public record ChatListItemModel(int Position, Chat.Chat? Chat, bool IsLastItemInBlock, bool IsFirstItem)
    : IVirtualListItem
{
    public static ChatListItemModel NewInviteFriendsBanner(int position)
        => new(position, null, false, false);

    public string Key { get; } = Position.ToString();
    public string RenderKey => Chat?.Id.Value ?? "invite-friends-banner";
    public bool IsGroup => false;
    public bool ShouldSkipKey => false;
    public bool HasRegularSize => Chat is not null && !IsLastItemInBlock;
}

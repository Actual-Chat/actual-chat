namespace ActualChat.UI.Blazor.App.Services;

// Chat is null for the two rows that aren't chats: the invite-friends banner that closes a short
// list, and a notification-history row, which carries its group instead
public record ChatListItemModel(
    int Position,
    Chat.Chat? Chat,
    bool IsLastItemInBlock,
    bool IsFirstItem,
    NotificationHistoryGroup? History = null)
    : IVirtualListItem
{
    public static ChatListItemModel NewInviteFriendsBanner(int position)
        => new(position, null, false, false);

    public static ChatListItemModel NewHistory(int position, NotificationHistoryGroup history)
        => new(position, null, false, false, history);

    public string Key { get; } = Position.ToString();
    public string RenderKey => History is { } history
        ? $"history:{history.ChatId.Value}"
        : Chat?.Id.Value ?? "invite-friends-banner";
    public bool IsGroup => false;
    public bool ShouldSkipKey => false;
    public bool HasRegularSize => (Chat is not null || History is not null) && !IsLastItemInBlock;
}

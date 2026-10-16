namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// What the share dialog shares. <see cref="InviteChatId"/> is set for those who may create invite links
/// (a Place root chat stands for its Place); <see cref="IsApprovalNeeded"/> marks a private target's own address.
/// </summary>
public sealed record ShareModalModel(
    ShareKind Kind,
    string Title,
    string TargetTitle,
    ShareRequest Request,
    IShareModalSelector? SelectorPrefs,
    string? ImageUrl = null,
    string Caption = "")
{
    public ChatId? InviteChatId { get; init; }
    public bool IsApprovalNeeded { get; init; }
}

public interface IShareModalSelector;

public sealed record ShareWithPlaceMembersOnly(PlaceId PlaceId) : IShareModalSelector
{
    public static ShareWithPlaceMembersOnly? GetFor(Chat.Chat chat)
        // The plain link of such a chat leads nowhere for anyone outside its Place
        => chat is { Id: PlaceChatId placeChatId, IsPublic: false, JoinRequestAccess: JoinRequestAccess.PlaceMembers }
            ? new ShareWithPlaceMembersOnly(placeChatId.PlaceId)
            : null;
}

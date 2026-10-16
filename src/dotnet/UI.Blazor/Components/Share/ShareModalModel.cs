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
    public static ShareWithPlaceMembersOnly? GetFor(Chat.Chat chat, Place? place)
    {
        // The plain link of such a chat leads nowhere for anyone outside its Place:
        // outsiders get the stricter of the chat's and the Place's access, as JoinRequests.GetTarget does
        if (chat is not { Id: PlaceChatId placeChatId, IsPublic: false } || place is null || place.IsPublic)
            return null;

        var outsiderAccess = chat.JoinRequestAccess.Min(place.JoinRequestAccess);
        return outsiderAccess.Allows(isSignedIn: true, isPlaceMember: false) ? null
            : new ShareWithPlaceMembersOnly(placeChatId.PlaceId);
    }
}

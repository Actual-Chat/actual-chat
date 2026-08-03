namespace ActualChat.UI.Blazor.App.Components;

public sealed record EditChatMemberModel(
    Author Author,
    bool IsOwner,
    bool IsModerator,
    bool IsOwn,
    bool CanPromoteToOwner,
    bool CanSetModerator,
    bool CanRemoveFromGroup);

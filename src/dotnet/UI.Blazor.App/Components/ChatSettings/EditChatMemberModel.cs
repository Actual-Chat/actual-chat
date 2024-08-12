namespace ActualChat.UI.Blazor.App.Components;

public sealed record EditChatMemberModel(
    Author Author,
    bool IsOwner,
    bool IsOwn,
    bool CanPromoteToOwner,
    bool CanRemoveFromGroup);

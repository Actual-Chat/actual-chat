namespace ActualChat.UI.Blazor.App.Components;

public sealed record EditPlaceMemberModel(
    Author Author,
    bool IsOwner,
    bool IsOwn,
    bool CanPromoteToOwner,
    bool CanRemoveFromGroup);

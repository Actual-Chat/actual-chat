namespace ActualChat.Chat.UI.Blazor.Components;

public sealed record EditPlaceMemberModel(
    Author Author,
    bool IsOwner,
    bool IsOwn,
    bool CanPromoteToOwner,
    bool CanRemoveFromGroup);

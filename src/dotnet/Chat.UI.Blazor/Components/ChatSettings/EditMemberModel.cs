namespace ActualChat.Chat.UI.Blazor.Components;

public sealed record EditMemberModel(
    Author Author,
    bool IsOwner,
    bool IsOwn,
    bool CanPromoteToOwner,
    bool CanRemoveFromGroup);

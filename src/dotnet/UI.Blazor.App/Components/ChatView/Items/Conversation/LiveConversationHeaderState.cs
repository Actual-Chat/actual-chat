namespace ActualChat.UI.Blazor.App.Components;

public sealed record LiveConversationHeaderState(
    string Title,
    string ParticipantsText,
    bool IsJoined = false,
    bool HasAttended = false,
    bool IsDissolving = false,
    bool CanExpand = false,
    bool IsAnyoneTalking = false);

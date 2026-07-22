namespace ActualChat.UI.Blazor.App.Components;

public sealed record LiveConversationHeaderState(
    string Title,
    string ParticipantsText,
    bool HasFoldedEntries,
    bool IsExpanded,
    bool IsJoined = false,
    bool HasOverlay = false,
    bool IsDissolving = false,
    bool CanExpand = false);

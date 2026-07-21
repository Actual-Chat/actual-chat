namespace ActualChat.UI.Blazor.App.Components;

public sealed record LiveConversationHeaderState(
    string Title,
    string ParticipantsText,
    bool HasFoldedEntries,
    bool IsExpanded);

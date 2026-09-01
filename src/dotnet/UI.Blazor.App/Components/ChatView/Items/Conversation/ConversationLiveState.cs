using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// Render state for a conversation block: the translated text plus whether it is a live
/// (in-progress) conversation and whether the current user has joined it.
/// </summary>
public sealed record ConversationLiveState(
    TranslatedConversation Conversation,
    bool IsLive,
    bool IsJoined,
    bool IsVoiceOnly,
    string ParticipantsText = "",
    bool HasFoldedEntries = false,
    bool IsExpanded = false,
    IReadOnlyList<PreviewEntry>? TailPreview = null,
    bool HasSummary = false,
    int SwallowedCount = 0,
    int RevealBatch = 0,
    bool IsAnyoneTalking = false,
    // True once the viewer has attended this block, and it stays true after they stop listening:
    // leaving is an audio decision, and the block keeps the colour that says they were there.
    bool HasOverlay = false);

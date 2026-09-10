using ActualChat.Live;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// Render state for a conversation card, including its preview and reveal affordance.
/// </summary>
public sealed record ConversationLiveState(
    TranslatedConversation Conversation,
    bool IsLive,
    bool IsVoiceOnly,
    IReadOnlyList<PreviewEntry>? TailPreview = null,
    int SwallowedCount = 0,
    bool IsAnyoneTalking = false,
    bool HasAttended = false)
{
    public bool HasSummary => !Conversation.Title.Text.IsNullOrEmpty();
    public int RevealBatch => Math.Min(LiveFoldMath.RevealBatchSize, SwallowedCount);
}

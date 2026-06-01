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
    bool IsVoiceOnly);

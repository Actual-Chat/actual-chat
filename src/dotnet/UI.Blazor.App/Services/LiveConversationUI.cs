using ActualChat.Live;
using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// UI-side facade for live conversations: the active block, the local "am I joined" signal
/// (drives per-viewer collapse/expand), and join/leave participation signaling to the server.
/// </summary>
public class LiveConversationUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private ILiveConversations LiveConversations => Hub.LiveConversations;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;

    [ComputeMethod]
    public virtual Task<LiveConversation?> Get(ChatId chatId, CancellationToken cancellationToken)
        => LiveConversations.Get(Session, chatId, cancellationToken);

    [ComputeMethod]
    public virtual async Task<bool> AmIInLiveConversation(ChatId chatId, CancellationToken cancellationToken)
    {
        var audio = await ChatAudioUI.GetState(chatId).ConfigureAwait(false);
        if (audio.IsListening || audio.IsRecording)
            return true;

        return await ChatVideoUI.IsWatching(chatId, cancellationToken).ConfigureAwait(false);
    }

    public Task SetParticipation(ChatId chatId, ParticipationKind kind, bool isActive, CancellationToken cancellationToken)
        => LiveConversations.SetParticipation(Session, chatId, kind, isActive, cancellationToken);
}

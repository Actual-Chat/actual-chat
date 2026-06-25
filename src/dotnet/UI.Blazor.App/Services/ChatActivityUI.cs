using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Centralizes the "live in this chat" signal for a chat so multiple surfaces (chat-list tile,
/// activity panel) read one source. "Live" follows recording (survives speech gaps); the talking
/// count follows active streams. Wraps <see cref="LiveStreamUI"/> + the recording registry.
/// </summary>
public class ChatActivityUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private LiveStreamUI LiveStreamUI => Hub.LiveStreamUI;
    private ILiveSessions LiveSessions => Hub.LiveSessions;

    [ComputeMethod]
    public virtual async Task<ChatActivity> Get(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.Value.IsNullOrEmpty())
            return ChatActivity.None;
        var talkingIds = await LiveStreamUI.GetStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        var isLive = talkingIds.Length > 0
            || await LiveSessions.HasRecorder(Session, chatId, cancellationToken).ConfigureAwait(false);
        return isLive
            ? new ChatActivity(true, talkingIds.Length)
            : ChatActivity.None;
    }
}

public readonly record struct ChatActivity(bool IsActive, int TalkingCount)
{
    public static readonly ChatActivity None = default;
}

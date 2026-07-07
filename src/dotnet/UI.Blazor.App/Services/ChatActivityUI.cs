using ActualChat.Live;
using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// The chat-list tile's "live in this chat" signal: a latched live session (2+ peers or a ringing
/// call) reports its current participant count; a lone streamer with no session yet reports as
/// "talking". Wraps <see cref="ILiveSessions"/> + <see cref="LiveStreamUI"/>.
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

        var liveSession = await LiveSessions.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        // Count only members actually present now — the Host/Owner group survives a leave, so a
        // Group-based count would keep an exited host; a closing session can also report none left.
        var participantCount = liveSession?.Members.Count(m =>
            m.IsMicOpen || m.HasCamera || m.HasScreenShare || m.IsListening) ?? 0;
        if (participantCount > 0)
            return new ChatActivity(true, IsLiveSession: true, participantCount);

        var talkingIds = await LiveStreamUI.GetStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        var isTalking = talkingIds.Length > 0
            || await LiveSessions.HasRecorder(Session, chatId, cancellationToken).ConfigureAwait(false);
        return isTalking
            ? new ChatActivity(true, IsLiveSession: false, talkingIds.Length)
            : ChatActivity.None;
    }
}

public readonly record struct ChatActivity(bool IsActive, bool IsLiveSession, int ParticipantCount)
{
    public static readonly ChatActivity None = default;
}

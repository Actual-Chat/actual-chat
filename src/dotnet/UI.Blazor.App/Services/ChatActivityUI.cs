using ActualChat.Live;
using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

public enum CallMapPanelTab { Call, Map }

/// <summary>
/// The per-chat "what's live right now" signal — a latched live session (2+ peers or a ringing
/// call), a lone talking streamer, or live location shares — plus the state of the shared
/// call/map panel presenting those activities (its Call/Map switch selection).
/// Wraps <see cref="ILiveSessions"/>, <see cref="LiveStreamUI"/> and <see cref="LocationUI"/>.
/// </summary>
public class ChatActivityUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private readonly MutableState<(ChatId ChatId, CallMapPanelTab Tab)?> _selectedPanelTab =
        hub.StateFactory.NewMutable(((ChatId ChatId, CallMapPanelTab Tab)?)null);
    private readonly MutableState<ChatId?> _panelHiddenChatId = hub.StateFactory.NewMutable((ChatId?)null);

    private LiveStreamUI LiveStreamUI => Hub.LiveStreamUI;
    private ILiveSessions LiveSessions => Hub.LiveSessions;
    private LocationUI LocationUI => Hub.LocationUI;
    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;

    [ComputeMethod]
    public virtual async Task<ChatActivity> Get(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.Value.IsNullOrEmpty())
            return ChatActivity.None;

        var locationParticipants = await LocationUI.ListParticipants(chatId, cancellationToken).ConfigureAwait(false);
        var locationSharerCount = locationParticipants.Count;

        var liveSession = await LiveSessions.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        // Count only members actually present now — the Host/Owner group survives a leave, so a
        // Group-based count would keep an exited host; a closing session can also report none left.
        var participantCount = liveSession?.Members.Count(m =>
            m.IsMicOpen || m.HasCamera || m.HasScreenShare || m.IsListening) ?? 0;
        if (participantCount > 0)
            return new ChatActivity(IsLiveSession: true, participantCount, locationSharerCount);

        var talkingIds = await LiveStreamUI.GetStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        var talkingCount = talkingIds.Length;
        // Own recorder may not be in the streaming author ids yet — count it as one talker.
        if (talkingCount == 0
            && await LiveSessions.HasRecorder(Session, chatId, cancellationToken).ConfigureAwait(false))
            talkingCount = 1;
        return new ChatActivity(IsLiveSession: false, talkingCount, locationSharerCount);
    }

    [ComputeMethod]
    public virtual async Task<CallMapPanelState> GetPanelState(
        ChatId chatId,
        CancellationToken cancellationToken = default)
    {
        // The call side keys on watching (the call view is open for this user), not on mere
        // call activity in the chat — a non-watcher's panel must not flip to an empty call view.
        var isWatching = await ChatVideoUI.IsWatching(chatId, cancellationToken).ConfigureAwait(false);
        var activity = await Get(chatId, cancellationToken).ConfigureAwait(false);
        // Hide (from the map ⋮ menu) hides only the map side here; the call side is
        // collapsed to the Live pill via ChatVideoUI's hidden mode, so hasCall must stay
        // on to keep the pill mounted. LiveLocationBanner brings the map back, the pill
        // brings the call view back.
        var isHidden = await IsPanelHidden(chatId, cancellationToken).ConfigureAwait(false);
        var hasCall = isWatching;
        var hasMap = activity.HasLiveLocation && !isHidden;
        var tab = (hasCall, hasMap) switch {
            (true, false) => CallMapPanelTab.Call,
            (false, true) => CallMapPanelTab.Map,
            (true, true) => await GetSelectedPanelTab(chatId, cancellationToken).ConfigureAwait(false)
                ?? CallMapPanelTab.Call,
            _ => CallMapPanelTab.Call,
        };
        return new CallMapPanelState(hasCall, hasMap, tab);
    }

    public void SelectPanelTab(ChatId chatId, CallMapPanelTab tab)
        => _selectedPanelTab.Value = (chatId, tab);

    [ComputeMethod]
    public virtual async Task<bool> IsPanelHidden(ChatId chatId, CancellationToken cancellationToken = default)
        => await _panelHiddenChatId.Use(cancellationToken).ConfigureAwait(false) == chatId;

    public void SetPanelHidden(ChatId chatId, bool isHidden)
    {
        // TODO: use dictionary
        if (isHidden)
            _panelHiddenChatId.Value = chatId;
        else if (_panelHiddenChatId.Value == chatId)
            _panelHiddenChatId.Value = null;
    }

    // Protected/internal methods

    [ComputeMethod]
    protected virtual async Task<CallMapPanelTab?> GetSelectedPanelTab(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var selection = await _selectedPanelTab.Use(cancellationToken).ConfigureAwait(false);
        return selection is { } s && s.ChatId == chatId ? s.Tab : null;
    }
}

// HasLiveConversation covers call-style activity only (session or talking) — the chat-list
// badge and the header call button key off it; location sharing is a parallel dimension.

public readonly record struct ChatActivity(
    bool IsLiveSession,
    int ParticipantCount,
    int LocationSharerCount)
{
    public static readonly ChatActivity None = default;
    public bool HasLiveConversation => ParticipantCount > 0;
    public bool HasLiveLocation => LocationSharerCount > 0;
}

public sealed record CallMapPanelState(bool HasCall, bool HasMap, CallMapPanelTab Tab)
{
    public static readonly CallMapPanelState None = new(false, false, CallMapPanelTab.Call);

    public bool HasBoth => HasCall && HasMap;
    public bool IsAnyActive => HasCall || HasMap;
}

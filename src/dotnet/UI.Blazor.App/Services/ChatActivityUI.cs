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
    private readonly MutableState<ChatId?> _mapHiddenChatId = hub.StateFactory.NewMutable((ChatId?)null);

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
            return new ChatActivity(true, IsLiveSession: true, participantCount, locationSharerCount);

        var talkingIds = await LiveStreamUI.GetStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        var isTalking = talkingIds.Length > 0
            || await LiveSessions.HasRecorder(Session, chatId, cancellationToken).ConfigureAwait(false);
        return isTalking
            ? new ChatActivity(true, IsLiveSession: false, talkingIds.Length, locationSharerCount)
            : new ChatActivity(false, false, 0, locationSharerCount);
    }

    [ComputeMethod]
    public virtual async Task<CallMapPanelState> GetPanelState(
        ChatId chatId,
        CancellationToken cancellationToken = default)
    {
        // The call side keys on watching (the call view is open for this user), not on mere
        // call activity in the chat — a non-watcher's panel must not flip to an empty call view.
        var hasCall = await ChatVideoUI.IsWatching(chatId, cancellationToken).ConfigureAwait(false);
        var activity = await Get(chatId, cancellationToken).ConfigureAwait(false);
        var isMapHidden = await IsMapPanelHidden(chatId, cancellationToken).ConfigureAwait(false);
        var hasMap = activity.HasLiveLocation && !isMapHidden;
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
    public virtual async Task<bool> IsMapPanelHidden(ChatId chatId, CancellationToken cancellationToken = default)
        => await _mapHiddenChatId.Use(cancellationToken).ConfigureAwait(false) == chatId;

    public void SetMapPanelHidden(ChatId chatId, bool isHidden)
    {
        if (isHidden)
            _mapHiddenChatId.Value = chatId;
        else if (_mapHiddenChatId.Value == chatId)
            _mapHiddenChatId.Value = null;
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

// IsActive covers call-style activity only (session or talking) — the chat-list badge and
// the header call button key off it; location sharing is a parallel dimension.

public readonly record struct ChatActivity(
    bool IsActive,
    bool IsLiveSession,
    int ParticipantCount,
    int LocationSharerCount)
{
    public static readonly ChatActivity None = default;
    public bool HasLiveLocation => LocationSharerCount > 0;
}

public sealed record CallMapPanelState(bool HasCall, bool HasMap, CallMapPanelTab Tab)
{
    public static readonly CallMapPanelState None = new(false, false, CallMapPanelTab.Call);

    public bool HasBoth => HasCall && HasMap;
    public bool IsAnyActive => HasCall || HasMap;
}

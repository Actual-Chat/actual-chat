using ActualChat.Live;
using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

public enum VisualActivityTab { Call, Map }

public enum VisualActivityPanelMode { Inline, Expanded, Collapsed, Hidden }

/// <summary>
/// The per-chat "what's live right now" signal — a latched live session (2+ peers or a ringing
/// call), a lone talking streamer, or live location shares — plus the state of the shared
/// visual activity panel presenting those activities (its mode and Call/Map tab selection).
/// Wraps <see cref="ILiveSessions"/>, <see cref="LiveStreamUI"/> and <see cref="LocationUI"/>.
/// </summary>
public class ChatActivityUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private readonly ConcurrentDictionary<ChatId, VisualActivityTab> _selectedPanelTabs = new();
    private readonly ConcurrentDictionary<ChatId, VisualActivityPanelMode> _panelModes = new();
    private readonly ConcurrentDictionary<ChatId, CallActivity> _lastCallActivities = new();

    private LiveStreamUI LiveStreamUI => Hub.LiveStreamUI;
    private ILiveSessions LiveSessions => Hub.LiveSessions;
    private LocationUI LocationUI => Hub.LocationUI;
    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;

    [ComputeMethod]
    public virtual async Task<VisualActivity> Get(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (chatId.Value.IsNullOrEmpty())
            return VisualActivity.None;

        var isWatching = await ChatVideoUI.IsWatching(chatId, cancellationToken).ConfigureAwait(false);
        var hasMap = await LocationUI.IsAnyoneSharing(chatId, cancellationToken).ConfigureAwait(false);
        var mode = await GetPanelMode(chatId, cancellationToken).ConfigureAwait(false);
        var tab = (isWatching, hasMap) switch {
            (true, false) => VisualActivityTab.Call,
            (false, true) => VisualActivityTab.Map,
            (true, true) => await GetSelectedPanelTab(chatId, cancellationToken).ConfigureAwait(false),
            _ => VisualActivityTab.Call,
        };
        return new VisualActivity(mode, isWatching, hasMap, tab);
    }

    [ComputeMethod(ConsolidationDelay = 0.2)]
    public virtual async Task<CallActivity> GetCallActivity(ChatId chatId, CancellationToken cancellationToken)
    {
        // Consolidated: stream start/stop, summary flow resumes and heartbeats rewrite the live
        // session state constantly, and almost none of that changes the two values below.
        if (chatId.Value.IsNullOrEmpty())
            return CallActivity.None;

        var liveSession = await LiveSessions.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        // Dialing registers its caller as a recorder right away, so they'd read as a live participant
        // and surface "1 · live" before anyone answers. Skipping just them keeps the tile silent for a
        // plain ring while anyone else active in the chat - unrelated to the call - still counts.
        var isDialing = liveSession?.Kind == LiveSessionKind.Dialing;
        var dialingHost = isDialing ? liveSession!.Host : default;
        // Count only members actually present now — the Host/Owner group survives a leave, so a
        // Group-based count would keep an exited host; a closing session can also report none left.
        var participantCount = liveSession?.Members.Count(m =>
            m.AuthorId != dialingHost
            && (m.IsMicOpen || m.HasCamera || m.HasScreenShare || m.IsListening)) ?? 0;
        if (participantCount > 0)
            return Remember(chatId, new CallActivity(true, IsLiveSession: true, participantCount, isDialing));

        var talkingIds = await LiveStreamUI.GetAudioStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        if (isDialing)
            talkingIds = talkingIds.Where(id => id != dialingHost).ToApiArray();
        // HasRecorder isn't attributable to an author, so while dialing it would only re-report the caller.
        var isTalking = talkingIds.Count > 0
            || (!isDialing
            && await LiveSessions.HasRecorder(Session, chatId, cancellationToken).ConfigureAwait(false));
        var talkingCount = talkingIds.Count;
        // Own recorder may not be in the streaming author ids yet — count it as one talker.
        if (talkingCount == 0
            && await LiveSessions.HasRecorder(Session, chatId, cancellationToken).ConfigureAwait(false))
            talkingCount = 1;
        return isTalking
            ? Remember(chatId, new CallActivity(true, IsLiveSession: false, talkingCount, isDialing))
            : Remember(chatId, new CallActivity(false, IsLiveSession: false, 0, isDialing));
    }

    [ComputeMethod]
    public virtual async Task<bool> HasOngoingCall(ChatId chatId, CancellationToken cancellationToken)
    {
        // Real call substance, unlike GetCallActivity's participant count, which includes the
        // user's own passive listening: anyone's speech or open mic, and video/screencast from
        // either side. All inputs degrade to false while offline, so this is safe to await
        // from list items.
        if (await LiveStreamUI.HasActivity(chatId, cancellationToken).ConfigureAwait(false))
            return true;
        if (await LiveStreamUI.HasRecorder(chatId, cancellationToken).ConfigureAwait(false))
            return true;
        if (await ChatVideoUI.HasRemoteStreams(chatId, cancellationToken).ConfigureAwait(false))
            return true;

        return await ChatVideoUI.GetOwnSourceKind(chatId, cancellationToken).ConfigureAwait(false) is not null;
    }

    [ComputeMethod]
    public virtual Task<VisualActivityPanelMode> GetPanelMode(
        ChatId chatId, CancellationToken cancellationToken = default)
        => Task.FromResult(_panelModes.GetValueOrDefault(chatId));

    public CallActivity GetLastKnownCallActivity(ChatId chatId)
    {
        // Stand-in for callers that must not await GetCallActivity - see ComputedExt.UseIfReady.
        return _lastCallActivities.GetValueOrDefault(chatId);
    }

    [ComputeMethod]
    protected virtual Task<VisualActivityTab> GetSelectedPanelTab(ChatId chatId, CancellationToken cancellationToken)
        => Task.FromResult(_selectedPanelTabs.GetValueOrDefault(chatId, VisualActivityTab.Call));

    public void SelectTab(ChatId chatId, VisualActivityTab tab)
    {
        if (_selectedPanelTabs.TryGetValue(chatId, out var oldTab) && oldTab == tab)
            return;

        _selectedPanelTabs[chatId] = tab;
        using (Invalidation.Begin())
            _ = GetSelectedPanelTab(chatId, default);
    }

    public void SetPanelMode(ChatId chatId, VisualActivityPanelMode mode)
    {
        if (_panelModes.GetValueOrDefault(chatId) == mode)
            return;

        _panelModes[chatId] = mode;
        using (Invalidation.Begin())
            _ = GetPanelMode(chatId, default);
    }

    public void TogglePanelMode(ChatId chatId, VisualActivityPanelMode mode, bool isOn)
    {
        // Turning a mode off returns the panel to Inline, but only when that mode is
        // still the current one — a stale "off" must not clobber a newer mode.
        if (isOn)
            SetPanelMode(chatId, mode);
        else if (_panelModes.GetValueOrDefault(chatId) == mode)
            SetPanelMode(chatId, VisualActivityPanelMode.Inline);
    }

    // Private methods

    private CallActivity Remember(ChatId chatId, CallActivity callActivity)
    {
        _lastCallActivities[chatId] = callActivity;
        return callActivity;
    }
}

public sealed record VisualActivity(
    VisualActivityPanelMode PanelMode, bool HasCall, bool HasMap, VisualActivityTab Tab)
{
    public static readonly VisualActivity None =
        new(VisualActivityPanelMode.Inline, false, false, VisualActivityTab.Call);

    public bool CanSwitch => PanelMode == VisualActivityPanelMode.Inline && HasCall && HasMap;
}

// ParticipantCount counts live-session members when IsLiveSession, streaming talkers otherwise.

public readonly record struct CallActivity(
    bool IsActive,
    bool IsLiveSession,
    int ParticipantCount,
    bool IsDialing = false)
{
    public static readonly CallActivity None = default;
    public bool HasLiveConversation => ParticipantCount > 0;
}

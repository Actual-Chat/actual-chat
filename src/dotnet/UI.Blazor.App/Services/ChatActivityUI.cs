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

    private LiveStreamUI LiveStreamUI => Hub.LiveStreamUI;
    private ILiveSessions LiveSessions => Hub.LiveSessions;
    private LocationUI LocationUI => Hub.LocationUI;
    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;

    [ComputeMethod]
    public virtual async Task<VisualActivity> Get(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (chatId.Value.IsNullOrEmpty())
            return VisualActivity.None;

        // TODO: review this ai slop
        // The call side keys on watching (the call view is open for this user), not on mere
        // call activity in the chat — a non-watcher's panel must not flip to an empty call view.
        var isWatching = await ChatVideoUI.IsWatching(chatId, cancellationToken).ConfigureAwait(false);
        var isAnyoneSharingLocation = await LocationUI.IsAnyoneSharing(chatId, cancellationToken).ConfigureAwait(false);
        var mode = await GetPanelMode(chatId, cancellationToken).ConfigureAwait(false);
        // Hidden hides only the map side: the call side collapses to its Live pill (VideoPanel
        // renders it in the Hidden mode), so HasCall must stay on to keep the pill mounted.
        // LiveLocationBanner brings the map back, the pill brings the call view back.
        var hasCall = isWatching;
        var hasMap = isAnyoneSharingLocation && mode is not VisualActivityPanelMode.Hidden;
        var tab = (hasCall, hasMap) switch {
            (true, false) => VisualActivityTab.Call,
            (false, true) => VisualActivityTab.Map,
            (true, true) => await GetSelectedPanelTab(chatId, cancellationToken).ConfigureAwait(false),
            _ => VisualActivityTab.Call,
        };
        return new VisualActivity(mode, hasCall, hasMap, tab);
    }

    [ComputeMethod]
    public virtual async Task<CallActivity> GetCallActivity(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.Value.IsNullOrEmpty())
            return CallActivity.None;

        var liveSession = await LiveSessions.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        // Count only members actually present now — the Host/Owner group survives a leave, so a
        // Group-based count would keep an exited host; a closing session can also report none left.
        var participantCount = liveSession?.Members.Count(m =>
            m.IsMicOpen || m.HasCamera || m.HasScreenShare || m.IsListening) ?? 0;
        if (participantCount > 0)
            return new CallActivity(IsLiveSession: true, participantCount);

        var talkingIds = await LiveStreamUI.GetStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        var talkingCount = talkingIds.Length;
        // Own recorder may not be in the streaming author ids yet — count it as one talker.
        if (talkingCount == 0
            && await LiveSessions.HasRecorder(Session, chatId, cancellationToken).ConfigureAwait(false))
            talkingCount = 1;
        return new CallActivity(IsLiveSession: false, talkingCount);
    }

    [ComputeMethod]
    public virtual Task<VisualActivityPanelMode> GetPanelMode(
        ChatId chatId, CancellationToken cancellationToken = default)
        => Task.FromResult(_panelModes.GetValueOrDefault(chatId));

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
}

public sealed record VisualActivity(
    VisualActivityPanelMode PanelMode, bool HasCall, bool HasMap, VisualActivityTab Tab)
{
    public static readonly VisualActivity None =
        new(VisualActivityPanelMode.Inline, false, false, VisualActivityTab.Call);

    public bool HasAnyActivity => HasCall || HasMap;
    public bool CanSwitch => PanelMode == VisualActivityPanelMode.Inline && HasAnyActivity;
}

// ParticipantCount counts live-session members when IsLiveSession, streaming talkers otherwise.

public readonly record struct CallActivity(bool IsLiveSession, int ParticipantCount)
{
    public static readonly CallActivity None = default;
    public bool HasLiveConversation => ParticipantCount > 0;
}

using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public enum VisualActivityTab { Call, Map }

public enum VisualActivityPanelMode { Inline, Expanded, Collapsed, Hidden }

/// <summary>
/// The per-chat "what's live right now" signal — a started live session (2+ peers or a ringing
/// call), a lone talking streamer, or live location shares — plus the state of the shared
/// visual activity panel presenting those activities (its mode and Call/Map tab selection).
/// Wraps <see cref="ILiveSessions"/>, <see cref="ISharedLocations"/>, etc.
/// </summary>
public class ChatActivityUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private readonly ConcurrentDictionary<ChatId, VisualActivityTab> _selectedPanelTabs = new();
    private readonly ConcurrentDictionary<ChatId, VisualActivityPanelMode> _panelModes = new();
    private readonly ConcurrentDictionary<ChatId, ChatLiveActivity?> _lastLiveActivities = new();

    private ILiveSessions LiveSessions => Hub.LiveSessions;
    private LocationUI LocationUI => Hub.LocationUI;
    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;
    private ConnectivityUI ConnectivityUI => Hub.ConnectivityUI;

    [ComputeMethod]
    public virtual async Task<VisualActivity> Get(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (chatId.Value.IsNullOrEmpty())
            return VisualActivity.None;

        var isWatching = await ChatVideoUI.IsWatching(chatId, cancellationToken).ConfigureAwait(false);
        var hasMap = await LocationUI.IsAnyoneSharing(chatId, cancellationToken).ConfigureAwait(false);
        var mode = await ChatVideoUI.GetShownPanelMode(chatId, cancellationToken).ConfigureAwait(false);
        var tab = (isWatching, hasMap) switch {
            (true, false) => VisualActivityTab.Call,
            (false, true) => VisualActivityTab.Map,
            (true, true) => await GetSelectedPanelTab(chatId, cancellationToken).ConfigureAwait(false),
            _ => VisualActivityTab.Call,
        };
        return new VisualActivity(mode, isWatching, hasMap, tab);
    }

    [ComputeMethod]
    public virtual async Task<ChatActivitySummary> GetSummary(ChatId chatId, CancellationToken cancellationToken = default)
    {
        if (chatId.Value.IsNullOrEmpty())
            return ChatActivitySummary.None;

        // While the RPC peer is down we stop receiving invalidations, so the last known value is stale
        var isConnected = await ConnectivityUI.IsConnected.Use(cancellationToken).ConfigureAwait(false);
        if (!isConnected)
            return ChatActivitySummary.None;

        // ReturnDefault serves null until the server answers
        var summary = await LiveSessions.GetActivitySummary(Session, chatId, cancellationToken).ConfigureAwait(false);
        return summary ?? ChatActivitySummary.None;
    }

    [ComputeMethod(ConsolidationDelay = 0)]
    public virtual async Task<ChatLiveActivity?> GetLiveActivity(ChatId chatId, CancellationToken cancellationToken)
    {
        // Consolidated: stream start/stop, heartbeats and typing all rewrite the summary,
        // and almost none of that changes the live activity.
        var summary = await GetSummary(chatId, cancellationToken).ConfigureAwait(false);
        _lastLiveActivities[chatId] = summary.LiveActivity;
        return summary.LiveActivity;
    }

    [ComputeMethod(ConsolidationDelay = 0)]
    public virtual async Task<bool> HasOngoingCall(ChatId chatId, CancellationToken cancellationToken)
    {
        // Real call substance, unlike GetLiveActivity's participant count, which includes the
        // user's own passive listening: anyone's speech, open mic or video, or the user's own camera
        // or screencast before its stream is up. Safe to await from list items: it's false offline.
        var summary = await GetSummary(chatId, cancellationToken).ConfigureAwait(false);
        if (summary.HasStreams)
            return true;

        return await ChatVideoUI.GetOwnSourceKind(chatId, cancellationToken).ConfigureAwait(false) is not null;
    }

    [ComputeMethod]
    public virtual Task<VisualActivityPanelMode> GetPanelMode(
        ChatId chatId, CancellationToken cancellationToken = default)
        => Task.FromResult(_panelModes.GetValueOrDefault(chatId));

    public ChatLiveActivity? GetLastKnownLiveActivity(ChatId chatId)
        // Stand-in for callers that must not await GetLiveActivity - see ComputedExt.UseIfReady.
        => _lastLiveActivities.GetValueOrDefault(chatId);

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
}

public sealed record VisualActivity(
    VisualActivityPanelMode PanelMode, bool HasCall, bool HasMap, VisualActivityTab Tab)
{
    public static readonly VisualActivity None =
        new(VisualActivityPanelMode.Inline, false, false, VisualActivityTab.Call);

    public bool CanSwitch => PanelMode == VisualActivityPanelMode.Inline && HasCall && HasMap;
}

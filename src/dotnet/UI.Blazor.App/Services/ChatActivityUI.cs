using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

public enum VisualActivityTab { Call, Map }

/// <summary>
/// The per-chat "what's live right now" signal — a latched live session (2+ peers or a ringing
/// call), a lone talking streamer, or live location shares — plus the state of the shared
/// visual activity panel presenting those activities (its Call/Map tab selection).
/// Wraps <see cref="ILiveSessions"/>, <see cref="LiveStreamUI"/> and <see cref="LocationUI"/>.
/// </summary>
public class ChatActivityUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private readonly ConcurrentDictionary<ChatId, VisualActivityTab> _selectedPanelTabs = new();
    private readonly ConcurrentDictionary<ChatId, Unit> _hiddenPanelChatIds = new();

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
        var isAnyoneSharingLocation = await LocationUI.IsAnyoneSharing(chatId, cancellationToken).ConfigureAwait(false);
        var isPanelVisible = await IsPanelVisible(chatId, cancellationToken).ConfigureAwait(false);
        var tab = await GetSelectedPanelTab(chatId, cancellationToken).ConfigureAwait(false);
        return new VisualActivity(isPanelVisible, isWatching, isAnyoneSharingLocation, tab);
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
    public virtual Task<bool> IsPanelVisible(ChatId chatId, CancellationToken cancellationToken = default)
        => Task.FromResult(!_hiddenPanelChatIds.ContainsKey(chatId));

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

    public void TogglePanel(ChatId chatId, bool isVisible)
    {
        var isChanged = isVisible
            ? _hiddenPanelChatIds.TryRemove(chatId, out _)
            : _hiddenPanelChatIds.TryAdd(chatId, default);
        if (!isChanged)
            return;

        using (Invalidation.Begin())
            _ = IsPanelVisible(chatId, default);
    }
}

public sealed record VisualActivity(bool IsPanelVisible, bool HasCall, bool HasMap, VisualActivityTab Tab)
{
    public static readonly VisualActivity None = new(false, false, false, VisualActivityTab.Call);
    public bool HasBoth => HasCall && HasMap;
}

// ParticipantCount counts live-session members when IsLiveSession, streaming talkers otherwise.

public readonly record struct CallActivity(bool IsLiveSession, int ParticipantCount)
{
    public static readonly CallActivity None = default;
    public bool HasLiveConversation => ParticipantCount > 0;
}

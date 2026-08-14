using ActualChat.UI.Blazor.Services;
using ActivityKind = ActualChat.UI.Blazor.Services.ActivityKind;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Platform-agnostic backend for <see cref="ActivitiesUI"/>: hands the current
/// <see cref="ActivitySet"/> to platform subclasses via <see cref="OnStateChanged"/>,
/// which render its Primary with the minimal OS-level UI.
/// </summary>
public class ActivitiesBackend : IDisposable
{
    private readonly ComputedState<ActivitySet?> _state;
    private readonly bool _isAndroidHost;
    private ActivitySet? _lastState;

    private AppUIHub Hub { get; }
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IncomingVoiceActivityUI IncomingVoiceActivityUI => Hub.IncomingVoiceActivityUI;
    private ActivitiesUI ActivitiesUI => field ??= Hub.Services.GetRequiredService<ActivitiesUI>();
    private LiveLocationReporter LiveLocationReporter
        => field ??= Hub.Services.GetRequiredService<LiveLocationReporter>();
    private ILogger Log => field ??= Hub.LogFor(GetType());

    public IState<ActivitySet?> State => _state;

    public ActivitiesBackend(AppUIHub hub)
    {
        Hub = hub;
        _isAndroidHost = hub.HostInfo.AppKind == AppKind.Android;
        _state = hub.StateFactory.NewComputed(
            new ComputedState<ActivitySet?>.Options() {
                InitialValue = null,
                UpdateDelayer = FixedDelayer.NextTick,
                Category = StateCategories.Get(GetType(), nameof(State)),
            },
            ComputeState);
        _state.Updated += OnStateUpdated;
    }

    public virtual void Dispose()
    {
        _state.Updated -= OnStateUpdated;
        _state.Dispose();
    }

    // Protected methods

    protected virtual void OnStateChanged(ActivitySet? state, ActivitySet? oldState)
    { }

    protected virtual void OnArmedChanged(bool isArmed)
    { }

    protected void InvokeAction(string actionName)
    {
        // Routes on what the notification is showing, not on ReplayState: a replay state whose
        // player isn't playing leaves the set showing something else entirely.
        if (_state.Value?.Primary is not { } primary)
            return;

        switch (primary.Kind) {
        case ActivityKind.Replaying:
            if (ChatAudioUI.ReplayState.Value is { } replayState)
                InvokeReplayAction(replayState, actionName);
            break;
        case ActivityKind.Listening:
            if (primary is AudioActivity { Chat: { } chat })
                InvokeListeningAction(chat.Id, actionName);
            break;
        case ActivityKind.SharingLocation:
            if (actionName == ActionNames.Stop)
                _ = BackgroundTask.Run(
                    () => LiveLocationReporter.StopAllSharing(CancellationToken.None),
                    Log, "StopAllSharing failed", CancellationToken.None);
            break;
        }
    }

    // Private methods

    private void OnStateUpdated(State state, StateEventKind eventKind)
    {
        if (eventKind != StateEventKind.Updated)
            return;

        var newState = _state.Value;
        var oldState = _lastState;
        if (Equals(newState, oldState))
            return;

        _lastState = newState;
        OnStateChanged(newState, oldState);
    }

    private async Task<ActivitySet?> ComputeState(CancellationToken cancellationToken)
    {
        // Read on every recompute: the host mirrors it so the next launch can raise the
        // foreground service before any of this state exists.
        var pttChatIds = _isAndroidHost
            ? await ChatAudioUI.GetPttChatIds(cancellationToken).ConfigureAwait(false)
            : [];
        OnArmedChanged(pttChatIds.Count > 0);

        var set = await ActivitiesUI.GetActivitySet(cancellationToken).ConfigureAwait(false);
        return set.IsEmpty ? null : set;
    }

    private void InvokeReplayAction(ReplayState state, string actionName)
    {
        switch (actionName) {
        case ActionNames.Stop:
            ChatAudioUI.StopReplay();
            break;
        case ActionNames.Pause:
            ChatAudioUI.GetReplayPlayerNonComputed(state.ChatId)?.Pause();
            break;
        case ActionNames.Resume:
            _ = ChatAudioUI.GetReplayPlayerNonComputed(state.ChatId)?.Resume();
            break;
        }
    }

    private void InvokeListeningAction(ChatId chatId, string actionName)
    {
        switch (actionName) {
        case ActionNames.Stop:
            IncomingVoiceActivityUI.ClearIncomingVoice(chatId);
            _ = ChatAudioUI.SetListeningState(chatId, false);
            break;
        case ActionNames.Pause:
            ChatAudioUI.GetListeningPlayerNonComputed(chatId)?.Pause();
            break;
        case ActionNames.Resume:
            _ = ChatAudioUI.GetListeningPlayerNonComputed(chatId)?.Resume();
            break;
        }
    }

    // Nested types

    protected static class ActionNames
    {
        public const string Stop = nameof(Stop);
        public const string Resume = nameof(Resume);
        public const string Pause = nameof(Pause);
    }
}

using ActualChat.Localization;
using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// UI-side facade for live conversations: the active block, the local "am I joined" signal
/// (drives per-viewer collapse/expand), and join/leave participation signaling to the server.
/// </summary>
public class LiveSessionUI(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub), IComputeService, INotifyInitialized
{
    // Refresh interval for active participations; must stay under the server's
    // ParticipantStaleness (90s) so a still-joined viewer never expires mid-call.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(45);
    // How long a mute verdict must survive before it stops a recording - long enough for a
    // peer's own mute lift to come back from the server, short enough to feel immediate.
    private static readonly TimeSpan MuteEnforcementDelay = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<ChatId, Conversation?> _lastConversations = new();
    private readonly ConcurrentDictionary<ChatId, LiveBlockState?> _lastBlockStates = new();

    private ILiveSessions LiveSessions => Hub.LiveSessions;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;
    private ActiveChatsUI ActiveChatsUI => Hub.ActiveChatsUI;

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod(ConsolidationDelay = 0, ConsolidationComparer = typeof(ConversationContentComparer))]
    public virtual async Task<Conversation?> GetConversation(ChatId chatId, CancellationToken cancellationToken)
    {
        // GetState churns far more often than the card it projects, and ToConversation() rebuilds it
        // every time, so the comparer is what lets this absorb the churn.
        var state = await LiveSessions.GetState(Session, chatId, cancellationToken).ConfigureAwait(false);
        return _lastConversations[chatId] = state is { SessionStartedAt: not null } ? state.ToConversation() : null;
    }

    [ComputeMethod(ConsolidationDelay = 0)]
    public virtual async Task<LiveBlockState?> GetBlockState(ChatId chatId, CancellationToken cancellationToken)
    {
        // Consolidated at the SOURCE deliberately: everything downstream of AmIInLiveConversation has to
        // stay immediately reactive, so the churn has to be absorbed here rather than on their outputs.
        var state = await LiveSessions.GetState(Session, chatId, cancellationToken).ConfigureAwait(false);
        return _lastBlockStates[chatId] = state is null
            ? null
            : new LiveBlockState(
                state.SessionStartedAt is not null,
                state.EffectiveVisibleStartLid,
                state.ContextStartLid,
                state.EndEntryLid,
                state.IsExpandedByDefault,
                state.LastSummaryAt.EpochOffsetTicks > 0,
                state.IsClosing);
    }

    public Task<Conversation?> UseConversationOrLastKnown(ChatId chatId, Task<Conversation?> conversationTask)
        => UseOrLastKnown(_lastConversations, chatId, conversationTask);

    public Task<LiveBlockState?> UseBlockStateOrLastKnown(ChatId chatId, Task<LiveBlockState?> blockStateTask)
        => UseOrLastKnown(_lastBlockStates, chatId, blockStateTask);

    [ComputeMethod]
    public virtual Task<LiveSessionState?> GetState(ChatId chatId, CancellationToken cancellationToken)
        => LiveSessions.GetState(Session, chatId, cancellationToken);

    [ComputeMethod]
    public virtual async Task<LiveSession?> Get(ChatId chatId, CancellationToken cancellationToken)
        => await LiveSessions.Get(Session, chatId, cancellationToken).ConfigureAwait(false);

    [ComputeMethod(ConsolidationDelay = 0.2)]
    public virtual async Task<bool> IsTranscriptionOn(ChatId chatId, CancellationToken cancellationToken)
    {
        var state = await LiveSessions.GetState(Session, chatId, cancellationToken).ConfigureAwait(false);
        return state?.TranscriptionOn ?? false;
    }

    public Task SetRules(ChatId chatId, SessionRules rules, CancellationToken cancellationToken)
        => LiveSessions.SetRules(Session, chatId, rules, cancellationToken);

    public Task MutePeer(ChatId chatId, AuthorId targetAuthorId, bool muted, CancellationToken cancellationToken)
        => LiveSessions.MutePeer(Session, chatId, targetAuthorId, muted, cancellationToken);

    public Task MuteAll(ChatId chatId, bool muted, CancellationToken cancellationToken)
        => LiveSessions.MuteAll(Session, chatId, muted, cancellationToken);

    public Task SetHost(ChatId chatId, AuthorId targetAuthorId, CancellationToken cancellationToken)
        => LiveSessions.SetHost(Session, chatId, targetAuthorId, cancellationToken);

    [ComputeMethod]
    public virtual async Task<bool> AmIInLiveConversation(ChatId chatId, CancellationToken cancellationToken)
    {
        var audio = await ChatAudioUI.GetState(chatId).ConfigureAwait(false);
        if (audio.IsListening || audio.IsRecording)
            return true;

        return await ChatVideoUI.IsWatching(chatId, cancellationToken).ConfigureAwait(false);
    }

    // The non-reactive form of AmIInLiveConversation, for callbacks that can't await.
    public bool IsInLiveConversation(ChatId chatId)
    {
        var activeChats = ActiveChatsUI.ActiveChats.Value;
        if (activeChats.TryGetValue(chatId, out var activeChat) && (activeChat.IsListening || activeChat.IsRecording))
            return true;

        return ChatVideoUI.WatchingChatId == chatId;
    }

    public Task SetParticipation(
        ChatId chatId,
        ParticipationKind kind,
        bool isActive,
        CancellationToken cancellationToken)
        => LiveSessions.SetParticipation(Session, chatId, kind, isActive, cancellationToken);

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        // Retried rather than awaited together: UIWorkerBase never restarts a worker that threw,
        // so one errored computed used to end participation reporting for the rest of the session.
        var baseChains = new[] {
            AsyncChain.From(RunParticipationSync),
            AsyncChain.From(RunMuteEnforcement),
        };
        var retryDelays = RetryDelaySeq.Exp(0.5, 8);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken);
    }

    private async Task RunParticipationSync(CancellationToken cancellationToken)
    {
        var cParticipations = await Computed
            .Capture(() => GetMyParticipations(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var current = new Dictionary<ChatId, ParticipationKind>();
        var lastHeartbeatAt = Clocks.CpuClock.Now;
        var isConnected = Hub.ConnectivityUI.IsConnected;
        var wasConnected = isConnected.Value;
        var isResendPending = false;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                // A reconnect re-sends everything: the server drops a peer's participations
                // once it stays disconnected past its grace, and the next heartbeat is up to 45s away.
                // The resend stays pending until a pass actually sends, so an errored computed doesn't eat it.
                isResendPending |= isConnected.Value && !wasConnected;
                wasConnected = isConnected.Value;
                // ValueOrDefault is null only when the computed errored; skipping the pass keeps
                // the reported participations until a recompute succeeds, where .Value would throw.
                if (cParticipations.ValueOrDefault is { } next) {
                    var now = Clocks.CpuClock.Now;
                    var isHeartbeat = isResendPending || now - lastHeartbeatAt >= HeartbeatInterval;
                    if (isHeartbeat) {
                        lastHeartbeatAt = now;
                        isResendPending = false;
                    }

                    foreach (var chatId in current.Keys.Except(next.Keys).ToList()) {
                        await SetParticipation(chatId, current[chatId], false, cancellationToken).ConfigureAwait(false);
                        current.Remove(chatId);
                    }
                    foreach (var (chatId, kind) in next)
                        if (isHeartbeat || !current.TryGetValue(chatId, out var existing) || existing != kind) {
                            await SetParticipation(chatId, kind, true, cancellationToken).ConfigureAwait(false);
                            current[chatId] = kind;
                        }
                }

                using var cts = cancellationToken.CreateLinkedTokenSource();
                var whenInvalidated = cParticipations.WhenInvalidated(cts.Token);
                var whenHeartbeat = Clocks.CpuClock.Delay(HeartbeatInterval, cts.Token);
                var whenConnectivityChanged = isConnected.Computed.WhenInvalidated(cts.Token);
                await Task.WhenAny(whenInvalidated, whenHeartbeat, whenConnectivityChanged).ConfigureAwait(false);
                cts.CancelAndDisposeSilently();
                cParticipations = await cParticipations.Update(cancellationToken).ConfigureAwait(false);
            }
        }
        finally {
            await ClearParticipations(current).ConfigureAwait(false);
        }
    }

    private async Task RunMuteEnforcement(CancellationToken cancellationToken)
    {
        // Soft mute enforcement: when the host turns off my recording (MicMuted) — either
        // per-peer or via mute-all — my own recorder stops and I'm told why. MicMuted is
        // peer-revocable: tapping record clears it (see RecorderToggle).
        var cMuted = await Computed
            .Capture(() => GetMutedRecordingChat(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested) {
            if (IsMuted(cMuted)) {
                // Tapping record lifts the mute server-side before it sets the recording intent,
                // but only the intent invalidates locally - the lifted session snapshot arrives a
                // round trip later. Acting on the first read would stop the recording it was for.
                await Clocks.CpuClock.Delay(MuteEnforcementDelay, cancellationToken).ConfigureAwait(false);
                cMuted = await cMuted.Update(cancellationToken).ConfigureAwait(false);
                if (IsMuted(cMuted)) {
                    await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(false);
                    Hub.ToastUI.Show(L.Call_RecordingTurnedOffByHost, "icon-mic-off", ToastDismissDelay.Short);
                }
            }

            await cMuted.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cMuted = await cMuted.Update(cancellationToken).ConfigureAwait(false);
        }
        return;

        // ValueOrDefault, not Value: an errored computed would throw and take the whole worker down.
        static bool IsMuted(Computed<ChatId?> computed)
            => computed.ValueOrDefault is { } chatId && !chatId.Value.IsNullOrEmpty();
    }

    // Protected/internal methods

    // It's internal to be accessible from tests
    internal LiveBlockState? GetLastKnownBlockState(ChatId chatId)
        => _lastBlockStates.GetValueOrDefault(chatId);

    [ComputeMethod]
    protected virtual async Task<ChatId?> GetMutedRecordingChat(CancellationToken cancellationToken)
    {
        var activeChats = await ActiveChatsUI.ActiveChats.Use(cancellationToken).ConfigureAwait(false);
        var recording = activeChats.FirstOrDefault(c => c.IsRecording);
        if (recording?.ChatId is not { } chatId || chatId.Value.IsNullOrEmpty())
            return null;

        var live = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (live is null)
            return null;

        var ownAuthor = await Hub.Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (ownAuthor is null)
            return null;

        var me = live.Members.FirstOrDefault(m => m.AuthorId == ownAuthor.Id);
        return me is { MicMuted: true } ? chatId : null;
    }

    [ComputeMethod]
    protected virtual async Task<ImmutableDictionary<ChatId, ParticipationKind>> GetMyParticipations(
        CancellationToken cancellationToken)
    {
        var result = ImmutableDictionary.CreateBuilder<ChatId, ParticipationKind>();
        var activeChats = await ActiveChatsUI.ActiveChats.Use(cancellationToken).ConfigureAwait(false);
        foreach (var chat in activeChats)
            if (chat.IsRecording)
                result[chat.ChatId] = ParticipationKind.Record;
            else if (chat.IsListening)
                result[chat.ChatId] = ParticipationKind.AudioListen;

        var watchingChatId = await ChatVideoUI.GetWatchingChatId(cancellationToken).ConfigureAwait(false);
        if (watchingChatId is { } videoChatId && !result.ContainsKey(videoChatId))
            result[videoChatId] = ParticipationKind.VideoView;

        return result.ToImmutable();
    }

    // Private methods

    private async Task ClearParticipations(Dictionary<ChatId, ParticipationKind> current)
    {
        // A scope torn down whole (app closed, headless session disposed) never reaches the loop's
        // own "chat left the set" branch, so the server kept believing we were here for the whole
        // 90s ParticipantStaleness - and suppressed every PTT wake as "already present".
        foreach (var (chatId, kind) in current)
            try {
                await SetParticipation(chatId, kind, false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Couldn't clear participation in chat #{ChatId}", chatId);
            }
    }

    private static Task<T?> UseOrLastKnown<T>(
        ConcurrentDictionary<ChatId, T?> lastKnownValues,
        ChatId chatId,
        Task<T?> task)
        where T : class
    {
        // Only covers a refetch in flight: with nothing to stand in on this returns the task, which
        // ILiveSessions' ReturnDefault mode completes with null rather than parking until reconnect.
        var computed = Computed.Current;
        if (computed is null || !lastKnownValues.TryGetValue(chatId, out var lastKnown))
            return task;

        return Task.FromResult(task.UseIfReady(lastKnown, computed));
    }
}

/// <summary>
/// The live-session fields the block and the chat view render from, projected so the rest of
/// <see cref="LiveSessionState"/> - participants, rules, ring state, activity - can churn without
/// invalidating them.
/// </summary>
public sealed record LiveBlockState(
    bool IsLatched,
    long VisibleStartLid,
    long ContextStartLid,
    long EndEntryLid,
    bool IsExpandedByDefault,
    bool HasSummary,
    bool IsClosing = false);

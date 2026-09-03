using ActualChat.Localization;
using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Module;
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
    // Give up watching if the call we just started never shows up as dialing.
    private static readonly TimeSpan DialingWaitTimeout = TimeSpan.FromSeconds(15);
    // How long a mute verdict must survive before it stops a recording - long enough for a
    // peer's own mute lift to come back from the server, short enough to feel immediate.
    private static readonly TimeSpan MuteEnforcementDelay = TimeSpan.FromSeconds(1);

    private static readonly string JSStartRingback = $"{BlazorUIAppModule.ImportName}.OutgoingCallRingback.start";
    private static readonly string JSStopRingback = $"{BlazorUIAppModule.ImportName}.OutgoingCallRingback.stop";

    private readonly ConcurrentDictionary<ChatId, CancellationTokenSource> _callWatches = new();
    private readonly ConcurrentDictionary<ChatId, Conversation?> _lastConversations = new();
    private readonly ConcurrentDictionary<ChatId, LiveBlockSnapshot?> _lastBlockSnapshots = new();
    private readonly Lock _ringbackLock = new();
    private object? _ringbackOwner;

    private ILiveSessions LiveSessions => Hub.LiveSessions;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;
    private AudioRecorder AudioRecorder => Hub.AudioRecorder;
    private ActiveChatsUI ActiveChatsUI => Hub.ActiveChatsUI;
    private Moment Now => Clocks.CpuClock.Now;

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
    public virtual async Task<LiveBlockSnapshot?> GetBlockSnapshot(ChatId chatId, CancellationToken cancellationToken)
    {
        // Consolidated at the SOURCE deliberately: everything downstream of AmIInLiveConversation has to
        // stay immediately reactive, so the churn has to be absorbed here rather than on their outputs.
        var state = await LiveSessions.GetState(Session, chatId, cancellationToken).ConfigureAwait(false);
        return _lastBlockSnapshots[chatId] = state is null
            ? null
            : new LiveBlockSnapshot(
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

    public Task<LiveBlockSnapshot?> UseSnapshotOrLastKnown(ChatId chatId, Task<LiveBlockSnapshot?> snapshotTask)
        => UseOrLastKnown(_lastBlockSnapshots, chatId, snapshotTask);

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

    public async Task StartCall(
        ChatId chatId,
        ApiArray<AuthorId> invitees,
        bool hasVideo,
        CancellationToken cancellationToken)
    {
        // Ask on the click itself: it's a real user gesture, the request can't yet race the ringback,
        // and JoinAnsweredCall's own check re-reads the (now cached) verdict without prompting again.
        // A call the caller can't be heard on isn't worth ringing the other side for, so a denial
        // stops it here instead of falling back to a listen-only call as the callee side does.
        if (!await AudioRecorder.MicrophonePermission.CheckOrRequest(cancellationToken).ConfigureAwait(false)) {
            Hub.ToastUI.Show(L.Call_NoMicrophoneAccess, "icon-phone-hang-up", ToastDismissDelay.Short);
            return;
        }

        try {
            await LiveSessions.StartCall(Session, chatId, invitees, hasVideo, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            // Only StandardError.Constraint (e.g. the peer-call gate) carries user-facing text.
            Log.LogWarning(e, "StartCall failed for chat #{ChatId}", chatId);
            var message = e is InvalidOperationException ? e.Message : L.Call_CouldntStart;
            Hub.ToastUI.Show(message, "icon-phone-hang-up", ToastDismissDelay.Short);
            return;
        }

        StartCallWatch(chatId);
    }

    public Task AcceptCall(ChatId chatId, CancellationToken cancellationToken)
        => LiveSessions.AcceptCall(Session, chatId, cancellationToken);

    public Task DeclineCall(ChatId chatId, CancellationToken cancellationToken)
        => LiveSessions.DeclineCall(Session, chatId, cancellationToken);

    public Task CancelCall(ChatId chatId, CancellationToken cancellationToken)
    {
        StopCallWatch(chatId);
        return LiveSessions.CancelCall(Session, chatId, cancellationToken);
    }

    [ComputeMethod]
    public virtual Task<CallStatus> GetCallStatus(ChatId chatId, CancellationToken cancellationToken)
        => LiveSessions.GetCallStatus(Session, chatId, cancellationToken);

    public Task DismissCallStatus(ChatId chatId, CancellationToken cancellationToken)
        => LiveSessions.DismissCallStatus(Session, chatId, cancellationToken);

    public Task LeaveCall(ChatId chatId, CancellationToken cancellationToken)
        => LiveSessions.LeaveCall(Session, chatId, cancellationToken);

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
        try {
            while (!cancellationToken.IsCancellationRequested) {
                // ValueOrDefault is null only when the computed errored; skipping the pass keeps
                // the reported participations until a recompute succeeds, where .Value would throw.
                if (cParticipations.ValueOrDefault is { } next) {
                    var now = Clocks.CpuClock.Now;
                    var isHeartbeat = now - lastHeartbeatAt >= HeartbeatInterval;
                    if (isHeartbeat)
                        lastHeartbeatAt = now;

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
                await Task.WhenAny(whenInvalidated, whenHeartbeat).ConfigureAwait(false);
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
    internal LiveBlockSnapshot? GetLastKnownBlockSnapshot(ChatId chatId)
        => _lastBlockSnapshots.GetValueOrDefault(chatId);

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

    private void StartCallWatch(ChatId chatId)
    {
        StopCallWatch(chatId);
        var cts = StopToken.CreateLinkedTokenSource();
        _callWatches[chatId] = cts;
        _ = WatchOutgoingCall(chatId, cts);
    }

    private void StopCallWatch(ChatId chatId)
    {
        if (_callWatches.TryRemove(chatId, out var cts))
            cts.CancelAndDisposeSilently();
    }

    private async Task WatchOutgoingCall(ChatId chatId, CancellationTokenSource cts)
    {
        var cancellationToken = cts.Token;
        var isRingbackOn = false;
        try {
            var watchStartedAt = Now;
            var isDialing = false;
            var computed = await Computed
                .Capture(() => Get(chatId, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            while (!cancellationToken.IsCancellationRequested) {
                var live = computed.Value;
                if (live is { Kind: LiveSessionKind.Dialing }) {
                    isDialing = true;
                    // The caller hears the ringback for as long as the call is dialing; the exits that
                    // don't stop it themselves (timeout, cancel) unwind through the finally.
                    if (!isRingbackOn) {
                        isRingbackOn = true;
                        StartRingback(cts);
                    }
                }
                else if (isDialing) {
                    // Dialing is over either way, so the tone goes now rather than in the finally:
                    // JoinAnsweredCall below awaits a mic permission prompt that can stay unanswered
                    // for as long as the user likes, and the ringback would play through all of it.
                    if (isRingbackOn) {
                        isRingbackOn = false;
                        StopRingback(cts);
                    }
                    // A session that outlives dialing was answered; a vanished one ended without one.
                    if (live is not null)
                        await JoinAnsweredCall(chatId, cancellationToken).ConfigureAwait(false);
                    return;
                }
                else if (Now - watchStartedAt > DialingWaitTimeout)
                    return;

                await computed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
                computed = await computed.Update(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "WatchOutgoingCall failed for chat #{ChatId}", chatId);
        }
        finally {
            if (isRingbackOn)
                StopRingback(cts);
            // Only our own registration: a restart has already replaced it by the time we unwind.
            _callWatches.TryRemove(new KeyValuePair<ChatId, CancellationTokenSource>(chatId, cts));
        }
    }

    private void StartRingback(object owner)
    {
        // One shared tone: a restarted watch takes it over instead of re-starting it, so the
        // cancelled watch's teardown can't silence the new one.
        lock (_ringbackLock) {
            var wasPlaying = _ringbackOwner is not null;
            _ringbackOwner = owner;
            if (wasPlaying)
                return;
        }

        _ = PlayRingback(true);
    }

    private void StopRingback(object owner)
    {
        lock (_ringbackLock) {
            if (!ReferenceEquals(_ringbackOwner, owner))
                return;

            _ringbackOwner = null;
        }

        _ = PlayRingback(false);
    }

    private async Task PlayRingback(bool start)
    {
        try {
            await Hub.JS.InvokeVoidAsync(start ? JSStartRingback : JSStopRingback).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Outgoing-call ringback {Action} failed", start ? "start" : "stop");
        }
    }

    private async Task JoinAnsweredCall(ChatId chatId, CancellationToken cancellationToken)
    {
        // Placing a call is itself the intent to talk, so answering it puts the caller on the line.
        // A denied mic still joins them — listening only, same as anywhere else.
        await ChatAudioUI.SetListeningState(chatId, true).ConfigureAwait(false);
        var hasMic = await AudioRecorder.MicrophonePermission
            .CheckOrRequest(cancellationToken)
            .ConfigureAwait(false);
        if (hasMic)
            await ChatAudioUI.SetRecordingChatId(chatId).ConfigureAwait(false);
    }

    private static Task<T?> UseOrLastKnown<T>(
        ConcurrentDictionary<ChatId, T?> lastKnownValues,
        ChatId chatId,
        Task<T?> task)
        where T : class
    {
        // The first read still awaits - standing in on nothing would flash a chat with no live block.
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
public sealed record LiveBlockSnapshot(
    bool IsLatched,
    long VisibleStartLid,
    long ContextStartLid,
    long EndEntryLid,
    bool IsExpandedByDefault,
    bool HasSummary,
    bool IsClosing = false);

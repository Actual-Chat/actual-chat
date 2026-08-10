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

    private static readonly string JSStartRingback = $"{BlazorUIAppModule.ImportName}.OutgoingCallRingback.start";
    private static readonly string JSStopRingback = $"{BlazorUIAppModule.ImportName}.OutgoingCallRingback.stop";

    private readonly ConcurrentDictionary<ChatId, CancellationTokenSource> _callWatches = new();
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
        return state is { SessionStartedAt: not null } ? state.ToConversation() : null;
    }

    [ComputeMethod(ConsolidationDelay = 0)]
    public virtual async Task<LiveBlockAnchors?> GetBlockAnchors(ChatId chatId, CancellationToken cancellationToken)
    {
        // The tile builder needs only these two lids, while GetState also churns on participants,
        // rules, ring state and activity - consolidating here keeps that churn out of the chat view.
        var state = await LiveSessions.GetState(Session, chatId, cancellationToken).ConfigureAwait(false);
        return state is { SessionStartedAt: not null }
            ? new LiveBlockAnchors(state.EffectiveVisibleStartLid, state.ContextStartLid)
            : null;
    }

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
        try {
            await LiveSessions.StartCall(Session, chatId, invitees, hasVideo, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            // Only StandardError.Constraint (e.g. the peer-call gate) carries user-facing text.
            Log.LogWarning(e, "StartCall failed for chat #{ChatId}", chatId);
            var message = e is InvalidOperationException ? e.Message : "Couldn't start the call";
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

    public Task SetParticipation(ChatId chatId, ParticipationKind kind, bool isActive, CancellationToken cancellationToken)
        => LiveSessions.SetParticipation(Session, chatId, kind, isActive, cancellationToken);

    protected override async Task OnRun(CancellationToken cancellationToken)
        => await Task.WhenAll(
            RunParticipationSync(cancellationToken),
            RunMuteEnforcement(cancellationToken)).ConfigureAwait(false);

    private async Task RunParticipationSync(CancellationToken cancellationToken)
    {
        var cParticipations = await Computed
            .Capture(() => GetMyParticipations(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var current = new Dictionary<ChatId, ParticipationKind>();
        var lastHeartbeatAt = Clocks.CpuClock.Now;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var next = cParticipations.Value;
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

    // Soft mute enforcement: when the host turns off my recording (MicMuted) — either
    // per-peer or via mute-all — my own recorder stops and I'm told why. MicMuted is
    // peer-revocable: tapping record clears it (see RecorderToggle).
    private async Task RunMuteEnforcement(CancellationToken cancellationToken)
    {
        var cMuted = await Computed
            .Capture(() => GetMutedRecordingChat(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested) {
            if (cMuted.Value is { } chatId && !chatId.Value.IsNullOrEmpty()) {
                await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(false);
                Hub.ToastUI.Show("Recording turned off by the host", "icon-mic-off", ToastDismissDelay.Short);
            }

            await cMuted.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cMuted = await cMuted.Update(cancellationToken).ConfigureAwait(false);
        }
    }

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
    protected virtual async Task<ImmutableDictionary<ChatId, ParticipationKind>> GetMyParticipations(CancellationToken cancellationToken)
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
        // 90s ParticipantStaleness - and suppressed every walkie wake as "already present".
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
        var ringbackOn = false;
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
                    // The caller hears the ringback for as long as the call is dialing; every exit
                    // below (answer, remote end, timeout, cancel) unwinds through the finally, which
                    // stops it.
                    if (!ringbackOn) {
                        ringbackOn = true;
                        StartRingback(cts);
                    }
                }
                else if (isDialing) {
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
            if (ringbackOn)
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
}

/// <summary>
/// The two lids the chat-view tile builder needs from a live session, projected so unrelated
/// live-state churn can be consolidated away before it reaches the view.
/// </summary>
public sealed record LiveBlockAnchors(long VisibleStartLid, long ContextStartLid);

using ActualChat.Localization;
using ActualChat.Live;
using ActualChat.Notifications;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Module;
using ActualLab.Diagnostics;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record IncomingCall(ChatId ChatId, AuthorId Caller, bool HasVideo);

/// <summary>
/// Client-side incoming-ring state: a push (or notification reconciliation) triggers
/// <see cref="OnRing"/>, but the reactive <see cref="LiveSessionUI.Get"/> is the source
/// of truth — a ring ends itself on cancel, timeout, decline, or accept on another device.
/// </summary>
public class IncomingCallUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    private static readonly string JSStartRingtone = $"{BlazorUIAppModule.ImportName}.IncomingCallRingtone.start";
    private static readonly string JSStopRingtone = $"{BlazorUIAppModule.ImportName}.IncomingCallRingtone.stop";

    private readonly Lock _ringingLock = new();
    private readonly MutableState<ImmutableList<ChatId>> _ringingChatIds;
    // Call mechanics state, the other half of Ringing above: the one chat I'm actively joined to
    // (only one at a time). Set the instant a join is committed, not once audio has started.
    private readonly MutableState<ChatId?> _inCallChatId;
    // Ring-time-only signal: OnRing sets it when the device is locked. Left stale once the ring/call
    // it names ends - OverLockChatId's derivation stops matching it by then either way.
    private readonly MutableState<ChatId?> _overLockRingChatId;
    // The raw candidate ForegroundCallChatId derives from; set at the same call sites as before.
    private readonly MutableState<ChatId?> _foregroundRawChatId;
    // OverLockChatId/ForegroundCallChatId: a presentation layer derived from the mechanics state
    // above (Ringing/InCall) plus Wide/Narrow and the over-lock signal, not independent state.
    private readonly ComputedState<ChatId?> _overLockChatId;
    private readonly ComputedState<ChatId?> _foregroundCallChatId;
    // The ring collapsed into the draggable island (foreground only); its modal is closed while set.
    private readonly MutableState<ChatId?> _collapsedChatId;
    // The ring whose ringtone the user silenced; the ring itself keeps going.
    private readonly MutableState<ChatId?> _mutedRingChatId;
    private int _ringGeneration;

    public IState<ChatId?> OverLockChatId => _overLockChatId;
    public IState<ChatId?> ForegroundCallChatId => _foregroundCallChatId;
    public IState<ChatId?> CollapsedChatId => _collapsedChatId;
    public IState<ChatId?> MutedRingChatId => _mutedRingChatId;

    private IIncomingCallsBridge? Bridge { get; }
    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IAuthors Authors => Hub.Authors;
    private INotifications Notifications => Hub.Notifications;
    private ILogger? CallDebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.AndroidIncomingCalls);

    public IncomingCallUI(AppUIHub hub) : base(hub)
    {
        Bridge = hub.Services.GetService<IIncomingCallsBridge>();
        _ringingChatIds = StateFactory.NewMutable(
            ImmutableList<ChatId>.Empty,
            StateCategories.Get(GetType(), "RingingChatIds"));
        _inCallChatId = StateFactory.NewMutable(
            (ChatId?)null,
            StateCategories.Get(GetType(), "InCallChatId"));
        _overLockRingChatId = StateFactory.NewMutable(
            (ChatId?)null,
            StateCategories.Get(GetType(), "OverLockRingChatId"));
        _foregroundRawChatId = StateFactory.NewMutable(
            (ChatId?)null,
            StateCategories.Get(GetType(), "ForegroundRawChatId"));
        _overLockChatId = StateFactory.NewComputed<ChatId?>(
            new ComputedState<ChatId?>.Options {
                UpdateDelayer = FixedDelayer.NextTick,
                Category = StateCategories.Get(GetType(), "OverLockChatId"),
            },
            ComputeOverLockChatId);
        _foregroundCallChatId = StateFactory.NewComputed<ChatId?>(
            new ComputedState<ChatId?>.Options {
                UpdateDelayer = FixedDelayer.NextTick,
                Category = StateCategories.Get(GetType(), "ForegroundCallChatId"),
            },
            ComputeForegroundCallChatId);
        Hub.RegisterDisposable(_overLockChatId);
        Hub.RegisterDisposable(_foregroundCallChatId);
        _collapsedChatId = StateFactory.NewMutable(
            (ChatId?)null,
            StateCategories.Get(GetType(), "CollapsedChatId"));
        _mutedRingChatId = StateFactory.NewMutable(
            (ChatId?)null,
            StateCategories.Get(GetType(), "MutedRingChatId"));
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    public void OnRing(ChatId chatId, bool showOverLockScreen = false)
    {
        if (chatId.Value.IsNullOrEmpty())
            return;

        CallDebugLog?.LogInformation("CALL_TRACE: OnRing #{ChatId}, showOverLockScreen={ShowOverLockScreen}",
            chatId, showOverLockScreen);
        lock (_ringingLock) {
            var chatIds = _ringingChatIds.Value;
            if (!chatIds.Contains(chatId))
                _ringingChatIds.Value = chatIds.Add(chatId);
        }
        if (showOverLockScreen)
            _overLockRingChatId.Value = chatId;
    }

    // Called by the over-lock call screen after it has rendered. The render callback fires before the
    // WebView actually paints, so wait a beat before removing the native cover — otherwise the app's
    // restored route flashes through for a frame on a cold start.
    public void OnOverLockScreenRendered()
    {
        CallDebugLog?.LogInformation("CALL_TRACE: OnOverLockScreenRendered");
        _ = RevealCallScreenAfterPaint();
    }

    private async Task RevealCallScreenAfterPaint()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
        CallDebugLog?.LogInformation("CALL_TRACE: RevealCallScreen (after paint delay), Bridge={HasBridge}",
            Bridge is not null);
        Bridge?.RevealCallScreen();
    }

    public void OnCallDismissed(ChatId chatId)
    {
        if (chatId.Value.IsNullOrEmpty())
            return;

        EndRing(chatId);
        _ = Bridge?.OnCallHandled(false);
    }

    [ComputeMethod]
    public virtual async Task<IncomingCall?> GetIncomingCall(CancellationToken cancellationToken)
    {
        var chatIds = await _ringingChatIds.Use(cancellationToken).ConfigureAwait(false);
        for (var i = chatIds.Count - 1; i >= 0; i--) {
            var call = await GetRingingCall(chatIds[i], cancellationToken).ConfigureAwait(false);
            if (call is not null)
                return call;
        }

        return null;
    }

    [ComputeMethod]
    public virtual async Task<IncomingCall?> GetRingingCall(ChatId chatId, CancellationToken cancellationToken)
    {
        var live = await LiveSessionUI.Get(chatId, cancellationToken).ConfigureAwait(false);
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var call = ownAuthor is null ? null : FindRingingCall(live, ownAuthor.Id);
        CallDebugLog?.LogInformation(
            "CALL_TRACE: GetRingingCall #{ChatId} → hasCall={HasCall}; liveNull={LiveNull}, "
            + "liveKind={Kind}, host={Host}, ownNull={OwnNull}, own={Own}, invites=[{Invites}]",
            chatId, call is not null, live is null, live?.Kind, live?.Host, ownAuthor is null, ownAuthor?.Id,
            live is null ? "" : live.Invites.Select(i => $"{i.InviteeId}:{i.Status}").ToDelimitedString(","));
        return call;
    }

    public static IncomingCall? FindRingingCall(LiveSession? live, AuthorId ownAuthorId)
    {
        // An incoming ring is the Dialing phase; once someone answers it's promoted to Call and is
        // no longer an incoming call.
        if (live is not { Kind: LiveSessionKind.Dialing })
            return null;
        if (live.Host == ownAuthorId)
            return null;

        var invite = live.Invites.FirstOrDefault(i => i.InviteeId == ownAuthorId);
        if (invite is not { Status: CallInviteStatus.Ringing })
            return null;

        return new IncomingCall(live.ChatId, live.Host, live.Rules.VideoAllowed);
    }

    public async Task Accept(ChatId chatId)
    {
        var isOverLockScreen = _overLockRingChatId.Value == chatId;
        var call = await GetRingingCall(chatId, default).ConfigureAwait(true);
        EndRing(chatId);
        if (call is null) {
            _ = Bridge?.OnCallHandled(false);
            Hub.ToastUI.Show(L.Call_Ended, "icon-phone", ToastDismissDelay.Short);
            return;
        }

        // A narrow view gets the same full-screen call view as over-lock instead of dropping straight
        // into the chat; DismissForegroundCall/HangUpForegroundCall (or its own auto-teardown) opens
        // the chat once it closes.
        var showsForegroundCall = !isOverLockScreen && Hub.BrowserInfo.ScreenSize.Value.IsNarrow();
        // Commit to InCall right away, before the accept RPC even starts - screen visibility, derived
        // from this, must not blink off between "ring ended" and "audio started".
        _inCallChatId.Value = chatId;
        try {
            await LiveSessionUI.AcceptCall(chatId, default).ConfigureAwait(true);
        }
        catch (Exception e) {
            _inCallChatId.Value = null;
            _ = Bridge?.OnCallHandled(false);
            Log.LogWarning(e, "AcceptCall failed for chat #{ChatId}", chatId);
            Hub.ToastUI.Show(L.Call_Ended, "icon-phone", ToastDismissDelay.Short);
            return;
        }

        try {
            // Accept over the lock screen keeps the call activity visible over the keyguard and starts
            // audio without unlocking: the mic FGS is allowed because the activity (shown via
            // SetShowWhenLocked) counts as foreground. Otherwise dismiss the keyguard first, since the
            // FGS can't start from a background state.
            var canStartAudio = isOverLockScreen
                || Bridge is null
                || await Bridge.OnCallHandled(true).ConfigureAwait(true);
            if (!isOverLockScreen && !showsForegroundCall)
                await Hub.History.NavigateTo(Links.Chat(chatId)).ConfigureAwait(true);
            if (canStartAudio) {
                var micPermission = Hub.AudioRecorder.MicrophonePermission;
                if (await micPermission.CheckOrRequest(CancellationToken.None).ConfigureAwait(true))
                    await ChatAudioUI.SetRecordingChatId(chatId).ConfigureAwait(true);
                else {
                    // Mic denied: still join the call as a listener.
                    await ChatAudioUI.SetListeningState(chatId, true).ConfigureAwait(true);
                }
            }
            if (showsForegroundCall)
                _foregroundRawChatId.Value = chatId;
        }
        catch {
            _inCallChatId.Value = null;
            throw;
        }
    }

    // Called before LiveSessionUI.JoinAnsweredCall starts audio - commits to InCall immediately,
    // mirroring Accept(), so the gap between "answered" and "audio started" is never visible.
    public void PrepareForegroundCall(ChatId chatId)
        => _inCallChatId.Value = chatId;

    // CancelPreparedCall undoes PrepareForegroundCall if starting the call's audio then failed.
    public void CancelPreparedCall(ChatId chatId)
    {
        if (_inCallChatId.Value == chatId)
            _inCallChatId.Value = null;
    }

    // Called once my own outgoing call is answered (LiveSessionUI.JoinAnsweredCall), after audio has
    // already started - same full-screen call view as an accepted incoming call, narrow layout only.
    public void ShowForegroundCall(ChatId chatId)
    {
        if (Hub.BrowserInfo.ScreenSize.Value.IsNarrow())
            _foregroundRawChatId.Value = chatId;
    }

    // Shows the full-screen call view for my own outgoing call while it's still dialing (narrow only),
    // so the caller sees "Dialing..." instead of nothing; PrepareForegroundCall flows it into InCall.
    public void ShowOutgoingCall(ChatId chatId)
    {
        if (Hub.BrowserInfo.ScreenSize.Value.IsNarrow())
            _foregroundRawChatId.Value = chatId;
    }

    // Hangs up my own still-dialing outgoing call from the full-screen view.
    public async Task CancelForegroundCall(ChatId chatId)
    {
        ClearForegroundCall(chatId);
        try {
            await LiveSessionUI.CancelCall(chatId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "CancelCall failed for chat #{ChatId}", chatId);
        }
    }

    public async Task Decline(ChatId chatId)
    {
        var isOverLockScreen = _overLockRingChatId.Value == chatId;
        ClearOverLock();
        EndRing(chatId);
        if (isOverLockScreen)
            Bridge?.MoveBehindLockScreen();
        else
            _ = Bridge?.OnCallHandled(false);
        try {
            await LiveSessionUI.DeclineCall(chatId, default).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "DeclineCall failed for chat #{ChatId}", chatId);
        }
    }

    // Silences / restores the ringtone without ending the ring: the call keeps ringing and the UI stays,
    // only the sound is toggled.
    public void ToggleMuteRing(ChatId chatId)
    {
        if (_mutedRingChatId.Value == chatId) {
            _mutedRingChatId.Value = null;
            StartRinging();
        }
        else {
            _mutedRingChatId.Value = chatId;
            StopRinging();
        }
    }

    // Collapses the ringing modal into the draggable island and silences the ringtone. The call keeps
    // ringing - the island's Accept/Decline still work and a tap on it re-opens the modal.
    public void Collapse(ChatId chatId)
    {
        if (_mutedRingChatId.Value != chatId) {
            _mutedRingChatId.Value = chatId;
            StopRinging();
        }
        _collapsedChatId.Value = chatId;
    }

    public void Expand(ChatId chatId)
    {
        if (_collapsedChatId.Value == chatId)
            _collapsedChatId.Value = null;
    }

    // "Message" action: decline the call and open the chat to type a reply instead.
    public async Task DeclineAndOpenChat(ChatId chatId)
    {
        await Decline(chatId).ConfigureAwait(true);
        await Hub.History.NavigateTo(Links.Chat(chatId)).ConfigureAwait(true);
    }

    // From the over-lock in-call screen: dismiss the keyguard (PIN) and, once unlocked, close the
    // in-call screen and open the chat. On a cancelled PIN we stay on the in-call screen.
    public async Task GoToChat(ChatId chatId)
    {
        var isUnlocked = Bridge is null || await Bridge.OnCallHandled(true).ConfigureAwait(true);
        if (!isUnlocked)
            return;

        ClearOverLock();
        await Hub.History.NavigateTo(Links.Chat(chatId)).ConfigureAwait(true);
    }

    public async Task HangUp(ChatId chatId)
    {
        ClearOverLock();
        Bridge?.MoveBehindLockScreen();
        await HangUpQuietly(chatId).ConfigureAwait(true);
    }

    // From the foreground in-call screen (narrow layout, not over the lock screen): just closes the
    // screen and opens the chat - unlike GoToChat, no keyguard/backgrounding call is involved.
    public Task DismissForegroundCall(ChatId chatId)
    {
        ClearForegroundCall(chatId);
        return Hub.History.NavigateTo(Links.Chat(chatId));
    }

    public async Task HangUpForegroundCall(ChatId chatId)
    {
        ClearForegroundCall(chatId);
        await HangUpQuietly(chatId).ConfigureAwait(true);
        await Hub.History.NavigateTo(Links.Chat(chatId)).ConfigureAwait(true);
    }

    [ComputeMethod]
    protected virtual async Task<bool> IsOverLockRingActive(CancellationToken cancellationToken)
    {
        var chatId = await _overLockRingChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return false;

        return await GetRingingCall(chatId, cancellationToken).ConfigureAwait(false) is not null;
    }

    [ComputeMethod]
    protected virtual async Task<bool> IsForegroundDialingActive(CancellationToken cancellationToken)
    {
        var chatId = await _foregroundRawChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return false;

        // CallStatus and the session Kind that commits _inCallChatId invalidate independently over RPC -
        // Accepted can land here before Kind == Call does, so it still counts as "dialing" too.
        var callStatus = await LiveSessionUI.GetCallStatus(chatId, cancellationToken).ConfigureAwait(false);
        return callStatus is CallStatus.Dialing or CallStatus.Accepted;
    }

    [ComputeMethod]
    protected virtual async Task<bool> IsInCallStillLive(CancellationToken cancellationToken)
    {
        var chatId = await _inCallChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return false;

        return await IsStillInCall(chatId, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<bool> IsRingingOrInCall(ChatId chatId, CancellationToken cancellationToken)
    {
        if (await GetRingingCall(chatId, cancellationToken).ConfigureAwait(false) is not null)
            return true;

        return await _inCallChatId.Use(cancellationToken).ConfigureAwait(false) == chatId;
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0.5, 10);
        return Task.WhenAll(
            AsyncChain.From(SyncRings)
                .Log(LogLevel.Debug, Log).RetryForever(retryDelays, Log).Run(cancellationToken),
            AsyncChain.From(SyncActiveCallNotifications)
                .Log(LogLevel.Debug, Log).RetryForever(retryDelays, Log).Run(cancellationToken),
            AsyncChain.From(ResetOverLockScreen)
                .Log(LogLevel.Debug, Log).RetryForever(retryDelays, Log).Run(cancellationToken),
            AsyncChain.From(ResetForegroundCallScreen)
                .Log(LogLevel.Debug, Log).RetryForever(retryDelays, Log).Run(cancellationToken),
            AsyncChain.From(ResetActiveCall)
                .Log(LogLevel.Debug, Log).RetryForever(retryDelays, Log).Run(cancellationToken),
            AsyncChain.From(SyncIncomingCallModal)
                .Log(LogLevel.Debug, Log).RetryForever(retryDelays, Log).Run(cancellationToken));
    }

    // Private methods

    private async Task<ChatId?> ComputeOverLockChatId(CancellationToken cancellationToken)
    {
        var chatId = await _overLockRingChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is not { } id)
            return null;

        return await IsRingingOrInCall(id, cancellationToken).ConfigureAwait(false) ? id : null;
    }

    private async Task<ChatId?> ComputeForegroundCallChatId(CancellationToken cancellationToken)
    {
        var chatId = await _foregroundRawChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is not { } id)
            return null;

        if (await IsRingingOrInCall(id, cancellationToken).ConfigureAwait(false))
            return id;

        // My own outgoing call, still dialing or just accepted (before PrepareForegroundCall commits it
        // to InCall - see IsForegroundDialingActive) - keep the "Dialing..." view up instead of nothing.
        var callStatus = await LiveSessionUI.GetCallStatus(id, cancellationToken).ConfigureAwait(false);
        return callStatus is CallStatus.Dialing or CallStatus.Accepted ? id : null;
    }

    private async Task<bool> IsStillInCall(ChatId chatId, CancellationToken cancellationToken)
    {
        // AmIInLiveConversation alone is local-only (am I recording/listening) - it stays true even
        // after the peer ends the call and the server drops the session, since nothing else tells my
        // own recorder to stop. Requiring the session to still be a Call is what detects that.
        var live = await LiveSessionUI.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (live is not { Kind: LiveSessionKind.Call })
            return false;

        return await LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken).ConfigureAwait(false);
    }

    private async Task SyncRings(CancellationToken cancellationToken)
    {
        if (Bridge is not null) {
            // A call push may have landed while the app was killed and the user opened it
            // from the launcher — pick the ring up from the still-active system notification.
            foreach (var chatId in await Bridge.ListActiveCallChatIds(cancellationToken).ConfigureAwait(false))
                OnRing(chatId);
        }

        var cCall = await Computed
            .Capture(() => GetIncomingCall(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var isRinging = false;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var call = cCall.Value;
                if (call is not null != isRinging) {
                    isRinging = call is not null;
                    if (isRinging)
                        StartRinging();
                    else
                        StopRinging();
                }
                await PruneDeadRings(cancellationToken).ConfigureAwait(false);

                await cCall.WhenInvalidated(cancellationToken).ConfigureAwait(false);
                cCall = await cCall.Update(cancellationToken).ConfigureAwait(false);
            }
        }
        finally {
            if (isRinging)
                StopRinging();
        }
    }

    // The ring to show as a foreground modal: null while it's shown over the lock screen (native view)
    // or collapsed into the island. Reactive to all three, so collapse/expand re-drive the modal.
    [ComputeMethod]
    protected virtual async Task<IncomingCall?> GetModalCall(CancellationToken cancellationToken)
    {
        var call = await GetIncomingCall(cancellationToken).ConfigureAwait(false);
        if (call is null)
            return null;

        var overLock = await _overLockChatId.Use(cancellationToken).ConfigureAwait(false);
        if (overLock == call.ChatId)
            return null;

        var collapsed = await _collapsedChatId.Use(cancellationToken).ConfigureAwait(false);
        if (collapsed == call.ChatId)
            return null;

        return call;
    }

    private async Task SyncIncomingCallModal(CancellationToken cancellationToken)
    {
        // The foreground incoming call surfaces as a modal (design), not the old top banner. It's skipped
        // while the ring is over the lock screen or collapsed into the island (see GetModalCall). The modal
        // closes itself when GetModalCall drops to null; the per-chat guard stops it re-popping.
        var cCall = await Computed
            .Capture(() => GetModalCall(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        ChatId? shownForChatId = null;
        while (!cancellationToken.IsCancellationRequested) {
            var call = cCall.Value;
            if (call is null)
                shownForChatId = null;
            else if (shownForChatId != call.ChatId) {
                shownForChatId = call.ChatId;
                var caller = call.Caller;
                // ModalUI.Show needs the Blazor dispatcher; this runs on a worker chain.
                _ = Hub.Dispatcher.InvokeAsync(
                    () => Hub.ModalUI.Show(new IncomingCallModal.Model(caller), cancellationToken));
            }

            await cCall.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cCall = await cCall.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private void StartRinging()
    {
        // Routes the ring melody to the platform ringer: the native bridge on Android, the looping web
        // ringtone everywhere else. Fire-and-forget to mirror the sync Bridge calls (and keep the finally
        // teardown sync); the JS invocation swallows its own errors.
        if (Bridge is not null)
            _ = StartNativeRinging(Interlocked.Increment(ref _ringGeneration));
        else
            _ = PlayWebRingtone(true);
    }

    private void StopRinging()
    {
        if (Bridge is not null) {
            // Bumped first: a start still waiting on the audio mode drops instead of ringing on.
            Interlocked.Increment(ref _ringGeneration);
            Bridge.StopRinging();
            _ = RestoreAudioMode();
        }
        else
            _ = PlayWebRingtone(false);
    }

    private async Task StartNativeRinging(int generation)
    {
        // The ringer stream follows the call route while the mode is InCommunication, so an armed
        // session holding it would put the whole ring in the earpiece. Nothing on the line - nothing
        // to protect: hand the mode back for the ring, exactly as a Normal-mode ring would sound.
        var liveChatIds = GetLiveAudioChatIds();
        Log.LogInformation("Incoming ring: live audio in [{ChatIds}]", liveChatIds.ToDelimitedString(","));
        if (liveChatIds.Count == 0) {
            try {
                await Hub.AudioFocusUI.YieldCommunicationMode().ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Couldn't yield the communication mode to the incoming ring");
            }
        }

        if (Volatile.Read(ref _ringGeneration) != generation)
            return;

        Bridge!.StartRinging();
    }

    private async Task RestoreAudioMode()
    {
        try {
            await Hub.AudioFocusUI.RestoreCommunicationMode().ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't restore the communication mode after the incoming ring");
        }
    }

    private List<ChatId> GetLiveAudioChatIds()
        => Hub.ActiveChatsUI.ActiveChats.Value
            .Where(c => c.IsListening || c.IsRecording)
            .Select(c => c.ChatId)
            .ToList();

    private async Task PlayWebRingtone(bool mustStart)
    {
        try {
            await Hub.JS.InvokeVoidAsync(mustStart ? JSStartRingtone : JSStopRingtone).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Web ringtone {Action} failed", mustStart ? "start" : "stop");
        }
    }

    private async Task SyncActiveCallNotifications(CancellationToken cancellationToken)
    {
        // The server's active-notification set reactively carries this user's rings — off Android it's
        // the primary trigger, on Android the safety net for a dropped push (a live scope only, so a
        // killed app still depends on it). GetRingingCall + PruneDeadRings confirm against the session.
        var cNotifications = await Computed
            .Capture(() => Notifications.ListActive(Session, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var c in cNotifications.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;

            foreach (var notification in c.Value)
                if (notification is CallNotification call)
                    OnRing(call.ChatId);
        }
    }

    private async Task PruneDeadRings(CancellationToken cancellationToken)
    {
        // Dead rings would otherwise accumulate for the whole scope lifetime; a still-live
        // second ring survives the prune and surfaces once the current one ends.
        ImmutableList<ChatId> chatIds;
        lock (_ringingLock)
            chatIds = _ringingChatIds.Value;
        foreach (var chatId in chatIds) {
            if (await GetRingingCall(chatId, cancellationToken).ConfigureAwait(false) is not null)
                continue;

            lock (_ringingLock)
                _ringingChatIds.Value = _ringingChatIds.Value.Remove(chatId);
            if (_collapsedChatId.Value == chatId)
                _collapsedChatId.Value = null;
            if (_mutedRingChatId.Value == chatId)
                _mutedRingChatId.Value = null;
        }
    }

    private async Task ResetOverLockScreen(CancellationToken cancellationToken)
    {
        // Tears down the over-lock screen when its ring ends unaccepted; a real accept also drops out
        // of GetRingingCall, but the _inCallChatId check below excludes that - ResetActiveCall's job.
        var cRingActive = await Computed
            .Capture(() => IsOverLockRingActive(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var wasActive = false;
        while (!cancellationToken.IsCancellationRequested) {
            var chatId = _overLockRingChatId.Value;
            if (cRingActive.Value)
                wasActive = true;
            else if (wasActive && chatId is not null && _inCallChatId.Value != chatId) {
                wasActive = false;
                CallDebugLog?.LogInformation(
                    "CALL_TRACE: ResetOverLockScreen teardown #{ChatId} (ring ended without accept)", chatId);
                ClearOverLock();
                Bridge?.MoveBehindLockScreen();
            }
            else
                wasActive = false;

            await cRingActive.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cRingActive = await cRingActive.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ResetForegroundCallScreen(CancellationToken cancellationToken)
    {
        // ResetOverLockScreen's counterpart for the narrow foreground screen's own phase with no
        // ring/native signal behind it: dialing out. InCall's teardown is ResetActiveCall's job.
        var cDialingActive = await Computed
            .Capture(() => IsForegroundDialingActive(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var wasActive = false;
        while (!cancellationToken.IsCancellationRequested) {
            var chatId = _foregroundRawChatId.Value;
            if (cDialingActive.Value)
                wasActive = true;
            else if (wasActive && chatId is not null && _inCallChatId.Value != chatId) {
                wasActive = false;
                CallDebugLog?.LogInformation(
                    "CALL_TRACE: ResetForegroundCallScreen teardown #{ChatId} (dialing ended unanswered)", chatId);
                _ = Hub.Dispatcher.InvokeAsync(() => HangUpForegroundCall(chatId));
            }
            else
                wasActive = false;

            await cDialingActive.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cDialingActive = await cDialingActive.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ResetActiveCall(CancellationToken cancellationToken)
    {
        // Ends a call mechanics-wise once the remaining side's session stops reporting Kind == Call,
        // regardless of which screen (if any) shows it - including the plain desktop header, previously uncovered.
        var cActive = await Computed
            .Capture(() => IsInCallStillLive(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var wasActive = false;
        while (!cancellationToken.IsCancellationRequested) {
            if (cActive.Value)
                wasActive = true;
            else if (wasActive && _inCallChatId.Value is { } chatId) {
                wasActive = false;
                CallDebugLog?.LogInformation("CALL_TRACE: ResetActiveCall teardown #{ChatId}", chatId);
                _ = Hub.Dispatcher.InvokeAsync(() => TeardownInCall(chatId));
            }

            await cActive.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cActive = await cActive.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private Task TeardownInCall(ChatId chatId)
    {
        if (_overLockRingChatId.Value == chatId)
            return HangUp(chatId);

        return _foregroundRawChatId.Value == chatId
            ? HangUpForegroundCall(chatId)
            : HangUpQuietly(chatId);
    }

    private void EndRing(ChatId chatId)
    {
        lock (_ringingLock) {
            var chatIds = _ringingChatIds.Value;
            if (chatIds.Contains(chatId))
                _ringingChatIds.Value = chatIds.Remove(chatId);
        }
        if (_collapsedChatId.Value == chatId)
            _collapsedChatId.Value = null;
        if (_mutedRingChatId.Value == chatId)
            _mutedRingChatId.Value = null;
        Bridge?.DismissCallNotification(chatId);
    }

    private void ClearOverLock()
        => _overLockRingChatId.Value = null;

    private void ClearForegroundCall(ChatId chatId)
    {
        if (_foregroundRawChatId.Value == chatId)
            _foregroundRawChatId.Value = null;
    }

    // Stops local audio and leaves the call mechanics-wise, with no screen-specific side effect - the
    // desktop plain-chat view has no call screen to close, so this is all it needs on hang-up.
    private async Task HangUpQuietly(ChatId chatId)
    {
        if (_inCallChatId.Value == chatId)
            _inCallChatId.Value = null;
        await StopCallAudio(chatId).ConfigureAwait(true);
        await LeaveCallQuietly(chatId).ConfigureAwait(true);
    }

    private async Task StopCallAudio(ChatId chatId)
    {
        await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(true);
        await ChatAudioUI.SetListeningState(chatId, false).ConfigureAwait(true);
    }

    private async Task LeaveCallQuietly(ChatId chatId)
    {
        try {
            await LiveSessionUI.LeaveCall(chatId, default).ConfigureAwait(true);
        }
        catch (Exception e) {
            Log.LogWarning(e, "LeaveCall failed for chat #{ChatId}", chatId);
        }
    }
}

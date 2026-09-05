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
    // The chat whose call surfaced over the lock screen; stays set through Accept (so the in-call
    // screen shows over the keyguard) until the user unlocks (GoToChat), hangs up, or the call ends.
    private readonly MutableState<ChatId?> _overLockChatId;
    // Held true across the Accept transition (ring ended, audio not yet started) so the over-lock
    // session doesn't momentarily read as ended and tear the screen down mid-accept.
    private readonly MutableState<bool> _isAccepting;
    // Same full-screen in-call view, shown in a narrow foreground layout instead of over the lock screen.
    private readonly MutableState<ChatId?> _foregroundCallChatId;
    // _isAccepting's counterpart for _foregroundCallChatId - same race, same fix.
    private readonly MutableState<bool> _isAcceptingForeground;
    // The ring collapsed into the draggable island (foreground only); its modal is closed while set.
    private readonly MutableState<ChatId?> _collapsedChatId;
    // The ring whose ringtone the user silenced; the ring itself keeps going.
    private readonly MutableState<ChatId?> _mutedRingChatId;
    private bool _overLockWasActive;
    private int _ringGeneration;

    // Long enough for a purely local Fusion recompute to settle - unlike over-lock, nothing here
    // forces a cross-process RPC round trip that would otherwise give the graph a natural pause.
    private static readonly TimeSpan ForegroundCallGraceDelay = TimeSpan.FromMilliseconds(300);

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
        _overLockChatId = StateFactory.NewMutable(
            (ChatId?)null,
            StateCategories.Get(GetType(), "OverLockChatId"));
        _foregroundCallChatId = StateFactory.NewMutable(
            (ChatId?)null,
            StateCategories.Get(GetType(), "ForegroundCallChatId"));
        _isAccepting = StateFactory.NewMutable(
            false,
            StateCategories.Get(GetType(), "IsAccepting"));
        _isAcceptingForeground = StateFactory.NewMutable(
            false,
            StateCategories.Get(GetType(), "IsAcceptingForeground"));
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
        if (showOverLockScreen) {
            Volatile.Write(ref _overLockWasActive, false); // Publication: the teardown loop polls it
            _overLockChatId.Value = chatId;
        }
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
        var isOverLockScreen = _overLockChatId.Value == chatId;
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
        // Hold the session "active" until audio starts, so the transition (ring ended, recording not
        // yet on) doesn't read as ended and tear the screen down before ChatAudioUI/Fusion catch up -
        // set before the RPC below so it's already visible once _foregroundCallChatId invalidates.
        if (isOverLockScreen)
            _isAccepting.Value = true;
        else if (showsForegroundCall)
            _isAcceptingForeground.Value = true;
        try {
            await LiveSessionUI.AcceptCall(chatId, default).ConfigureAwait(true);
        }
        catch (Exception e) {
            _isAccepting.Value = false;
            _isAcceptingForeground.Value = false;
            _ = Bridge?.OnCallHandled(false);
            Log.LogWarning(e, "AcceptCall failed for chat #{ChatId}", chatId);
            Hub.ToastUI.Show(L.Call_Ended, "icon-phone", ToastDismissDelay.Short);
            return;
        }

        // Anything failing past this point must still release the accepting flags: otherwise the
        // call screen reads as active forever and can never be torn down.
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
                _foregroundCallChatId.Value = chatId;
        }
        finally {
            _isAccepting.Value = false;
            ClearAcceptingForegroundEventually();
        }
    }

    // Called before LiveSessionUI.JoinAnsweredCall starts audio for my own answered outgoing call -
    // same _isAcceptingForeground bridge as Accept, guarding the same race.
    public void PrepareForegroundCall(ChatId chatId)
    {
        if (Hub.BrowserInfo.ScreenSize.Value.IsNarrow())
            _isAcceptingForeground.Value = true;
    }

    // Called once my own outgoing call is answered (LiveSessionUI.JoinAnsweredCall), after audio has
    // already started - same full-screen call view as an accepted incoming call, narrow layout only.
    public void ShowForegroundCall(ChatId chatId)
    {
        if (Hub.BrowserInfo.ScreenSize.Value.IsNarrow())
            _foregroundCallChatId.Value = chatId;
        ClearAcceptingForegroundEventually();
    }

    // Shows the full-screen call view for my own outgoing call while it's still dialing (narrow only),
    // so the caller sees "Dialing..." instead of nothing. It flows straight into the in-call view once
    // JoinAnsweredCall sets the same _foregroundCallChatId; IsForegroundCallActive keeps it up meanwhile.
    public void ShowOutgoingCall(ChatId chatId)
    {
        if (Hub.BrowserInfo.ScreenSize.Value.IsNarrow())
            _foregroundCallChatId.Value = chatId;
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
        var isOverLockScreen = _overLockChatId.Value == chatId;
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
        await StopCallAudio(chatId).ConfigureAwait(true);
        await LeaveCallQuietly(chatId).ConfigureAwait(true);
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
        await StopCallAudio(chatId).ConfigureAwait(true);
        await LeaveCallQuietly(chatId).ConfigureAwait(true);
        await Hub.History.NavigateTo(Links.Chat(chatId)).ConfigureAwait(true);
    }

    [ComputeMethod]
    protected virtual async Task<bool> IsOverLockSessionActive(CancellationToken cancellationToken)
    {
        var chatId = await _overLockChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return false;

        if (await _isAccepting.Use(cancellationToken).ConfigureAwait(false))
            return true;

        if (await GetRingingCall(chatId, cancellationToken).ConfigureAwait(false) is not null)
            return true;

        var inCall = await IsStillInCall(chatId, cancellationToken).ConfigureAwait(false);
        CallDebugLog?.LogInformation(
            "CALL_TRACE: IsOverLockSessionActive #{ChatId} → inCall={InCall} (ring not confirmed)",
            chatId, inCall);
        return inCall;
    }

    [ComputeMethod]
    protected virtual async Task<bool> IsForegroundCallActive(CancellationToken cancellationToken)
    {
        var chatId = await _foregroundCallChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return false;

        if (await _isAcceptingForeground.Use(cancellationToken).ConfigureAwait(false))
            return true;

        // My own outgoing call keeps the screen up while it's still dialing; once it's answered the
        // accepting-foreground grace above and IsStillInCall below take over, and a declined/unanswered
        // call drops out of Dialing so the screen tears down.
        var callStatus = await LiveSessionUI.GetCallStatus(chatId, cancellationToken).ConfigureAwait(false);
        if (callStatus == CallStatus.Dialing)
            return true;

        return await IsStillInCall(chatId, cancellationToken).ConfigureAwait(false);
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
            AsyncChain.From(SyncIncomingCallModal)
                .Log(LogLevel.Debug, Log).RetryForever(retryDelays, Log).Run(cancellationToken));
    }

    // Private methods

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
        // Tears down the over-lock screen once its session ends without the user unlocking (cancelled
        // ring, timeout, remote hang-up): closes the screen and sends the app behind the lock screen.
        // Guarded by _overLockWasActive so the cold-start load window (ring not yet confirmed) doesn't
        // fire early. The accept transition can't misfire here because _isAccepting keeps the session
        // active until audio starts. Direct exits (Decline/HangUp/GoToChat) clear the flag themselves.
        var cActive = await Computed
            .Capture(() => IsOverLockSessionActive(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested) {
            if (cActive.Value)
                Volatile.Write(ref _overLockWasActive, true);
            else if (Volatile.Read(ref _overLockWasActive) && _overLockChatId.Value is { } chatId) {
                CallDebugLog?.LogInformation(
                    "CALL_TRACE: ResetOverLockScreen teardown #{ChatId} (session ended, was active)",
                    chatId);
                await HangUp(chatId).ConfigureAwait(false);
            }

            await cActive.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cActive = await cActive.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ResetForegroundCallScreen(CancellationToken cancellationToken)
    {
        // ResetOverLockScreen's counterpart for the narrow-layout foreground screen.
        var cActive = await Computed
            .Capture(() => IsForegroundCallActive(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested) {
            if (!cActive.Value && _foregroundCallChatId.Value is { } chatId) {
                CallDebugLog?.LogInformation("CALL_TRACE: ResetForegroundCallScreen teardown #{ChatId}", chatId);
                _ = Hub.Dispatcher.InvokeAsync(() => HangUpForegroundCall(chatId));
            }

            await cActive.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cActive = await cActive.Update(cancellationToken).ConfigureAwait(false);
        }
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
    {
        Volatile.Write(ref _overLockWasActive, false); // Publication: the teardown loop polls it
        _isAccepting.Value = false;
        _overLockChatId.Value = null;
    }

    private void ClearForegroundCall(ChatId chatId)
    {
        if (_foregroundCallChatId.Value == chatId)
            _foregroundCallChatId.Value = null;
    }

    private void ClearAcceptingForegroundEventually()
        => _ = BackgroundTask.Run(async () => {
            await Clocks.CpuClock.Delay(ForegroundCallGraceDelay, CancellationToken.None)
                .ConfigureAwait(false);
            _isAcceptingForeground.Value = false;
        }, Log, "ClearAcceptingForegroundEventually failed", CancellationToken.None);

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

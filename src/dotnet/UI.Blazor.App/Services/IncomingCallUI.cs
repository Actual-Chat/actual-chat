using ActualChat.Localization;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Module;
using ActualLab.Diagnostics;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Screens and ringing for the call <see cref="CallUI"/> holds: the incoming modal, the island, the
/// over-lock and narrow full-screen views, the ringtone. The call mechanics live in <see cref="CallUI"/>.
/// </summary>
public class IncomingCallUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    private static readonly string JSStartRingtone = $"{BlazorUIAppModule.ImportName}.IncomingCallRingtone.start";
    private static readonly string JSStopRingtone = $"{BlazorUIAppModule.ImportName}.IncomingCallRingtone.stop";

    // Ring-time-only signal: OnRing sets it when the device is locked. It can outlive its ring, so
    // OverLockChatId honors it only while that chat holds the slot with an incoming call.
    private readonly MutableState<ChatId?> _overLockRingChatId;
    // The raw candidate ForegroundCallChatId derives from.
    private readonly MutableState<ChatId?> _foregroundRawChatId;
    // The two raw flags above, kept only while CallUI holds that chat.
    private readonly ComputedState<ChatId?> _overLockChatId;
    private readonly ComputedState<ChatId?> _foregroundCallChatId;
    // The ring collapsed into the draggable island (foreground only); its modal is closed while set.
    private readonly MutableState<ChatId?> _collapsedChatId;
    // My own outgoing call, collapsed into the draggable island (wide screens only); its modal is
    // closed while set - the outgoing-call counterpart of _collapsedChatId above.
    private readonly MutableState<ChatId?> _collapsedOutgoingChatId;
    // The ring whose ringtone the user silenced; the ring itself keeps going.
    private readonly MutableState<ChatId?> _mutedRingChatId;
    private int _ringGeneration;

    public IState<ChatId?> OverLockChatId => _overLockChatId;
    public IState<ChatId?> ForegroundCallChatId => _foregroundCallChatId;
    public IState<ChatId?> CollapsedChatId => _collapsedChatId;
    public IState<ChatId?> CollapsedOutgoingChatId => _collapsedOutgoingChatId;
    public IState<ChatId?> MutedRingChatId => _mutedRingChatId;

    private IIncomingCallsBridge? Bridge { get; }
    private CallUI CallUI => Hub.CallUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private ILogger? CallDebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.AndroidIncomingCalls);

    public IncomingCallUI(AppUIHub hub) : base(hub)
    {
        Bridge = hub.Services.GetService<IIncomingCallsBridge>();
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
        _collapsedOutgoingChatId = StateFactory.NewMutable(
            (ChatId?)null,
            StateCategories.Get(GetType(), "CollapsedOutgoingChatId"));
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
        CallUI.AddCandidate(chatId);
        // A ring that can't hold the slot must not take the screen over the lock: it would swap the held
        // call's screen for its own.
        var slotChatId = CallUI.GetCallChatIdNonComputed();
        if (showOverLockScreen && (slotChatId is null || slotChatId == chatId))
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
        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        return call is { Origin: CallOrigin.Incoming, Phase: CallPhase.Ringing, PeerId: { } callerId }
            ? new IncomingCall(call.ChatId, callerId, call.HasVideo)
            : null;
    }

    public async Task Accept(ChatId chatId)
    {
        var isOverLockScreen = _overLockRingChatId.Value == chatId;
        // Straight from the session, not the slot: Answer on an Android notification can land before the
        // search claimed the ring.
        var call = await CallUI.GetRingingCall(chatId, default).ConfigureAwait(true);
        if (call is null) {
            EndRing(chatId);
            _ = Bridge?.OnCallHandled(false);
            Hub.ToastUI.Show(L.Call_Ended, "icon-phone", ToastDismissDelay.Short);
            return;
        }

        // Committed before the ring is dropped and before the accept RPC starts: screen visibility, derived
        // from the slot, must not blink off between "ring ended" and "audio started".
        if (!CallUI.TryCommitAccept(call)) {
            Hub.ToastUI.Show(L.Call_AlreadyInCall, "icon-phone", ToastDismissDelay.Short);
            return;
        }

        ClearRingFlags(chatId);
        Bridge?.DismissCallNotification(chatId);
        // A narrow view gets the same full-screen call view as over-lock instead of dropping straight
        // into the chat; DismissForegroundCall/HangUpForegroundCall (or its own auto-teardown) opens
        // the chat once it closes.
        var showsForegroundCall = !isOverLockScreen && Hub.BrowserInfo.ScreenSize.Value.IsNarrow();
        try {
            await CallUI.AcceptCall(chatId, default).ConfigureAwait(true);
        }
        catch (Exception e) {
            CallUI.Release(chatId);
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
                // Listen unconditionally first; pending OS mic prompt won't gate EnforceCallConnectGrace check.
                await ChatAudioUI.SetListeningState(chatId, true).ConfigureAwait(true);
                var micPermission = Hub.AudioRecorder.MicrophonePermission;
                if (await micPermission.CheckOrRequest(CancellationToken.None).ConfigureAwait(true))
                    await ChatAudioUI.SetRecordingChatId(chatId).ConfigureAwait(true);
            }
            if (showsForegroundCall)
                _foregroundRawChatId.Value = chatId;
        }
        catch {
            CallUI.Release(chatId);
            throw;
        }
    }

    // Collapses the outgoing-call modal into the draggable island. The call keeps dialing - the
    // island's hang-up still works and a tap on it re-opens the modal.
    public void CollapseOutgoing(ChatId chatId)
        => _collapsedOutgoingChatId.Value = chatId;

    public void ExpandOutgoing(ChatId chatId)
    {
        if (_collapsedOutgoingChatId.Value != chatId)
            return;

        // Closing the modal (an explicit hang-up or collapsing itself) disposes that component
        // instance - clearing the flag alone won't bring it back, so re-show it here. If dialing has
        // since ended, the fresh instance's own ComputeState closes it right back (see its "resolved
        // close" branch).
        _collapsedOutgoingChatId.Value = null;
        ShowOutgoingCallModal(chatId);
    }

    // Hangs up my own still-dialing outgoing call from the full-screen view.
    public async Task CancelForegroundCall(ChatId chatId)
    {
        ClearForegroundCall(chatId);
        try {
            await CallUI.CancelCall(chatId, CancellationToken.None).ConfigureAwait(false);
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
            await CallUI.DeclineCall(chatId, default).ConfigureAwait(false);
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

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(SyncRings),
            AsyncChain.From(SyncCallScreens),
            AsyncChain.From(SyncIncomingCallModal),
        };
        var retryDelays = RetryDelaySeq.Exp(0.5, 10);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).Run(cancellationToken);
    }

    // Private methods

    private async Task<ChatId?> ComputeOverLockChatId(CancellationToken cancellationToken)
    {
        var chatId = await _overLockRingChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return null;

        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        return call is { Origin: CallOrigin.Incoming } && call.ChatId == chatId ? chatId : null;
    }

    private async Task<ChatId?> ComputeForegroundCallChatId(CancellationToken cancellationToken)
    {
        var chatId = await _foregroundRawChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return null;

        return await CallUI.GetCallChatId(cancellationToken).ConfigureAwait(false) == chatId ? chatId : null;
    }

    private async Task SyncCallScreens(CancellationToken cancellationToken)
    {
        var cInput = await Computed
            .Capture(() => GetScreenInput(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var last = new ScreenInput(null, null);
        while (!cancellationToken.IsCancellationRequested) {
            var input = cInput.Value;
            if (input != last) {
                OnScreenInputChanged(last, input);
                last = input;
            }

            await cInput.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cInput = await cInput.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnScreenInputChanged(ScreenInput last, ScreenInput input)
    {
        if (input.DialingOutChatId is { } dialingChatId && dialingChatId != last.DialingOutChatId)
            ShowOutgoingCall(dialingChatId);
        var (lastCall, call) = (last.Call, input.Call);
        if (call is { Origin: CallOrigin.Outgoing, Phase: CallPhase.Active }
            && lastCall is { Phase: CallPhase.Dialing }
            && lastCall.ChatId == call.ChatId)
            ShowForegroundCall(call.ChatId);
        if (lastCall is null || lastCall.ChatId == call?.ChatId)
            return;

        var chatId = lastCall.ChatId;
        CallDebugLog?.LogInformation("CALL_TRACE: slot released #{ChatId} from {Phase}", chatId, lastCall.Phase);
        ClearRingFlags(chatId);
        if (_collapsedOutgoingChatId.Value == chatId)
            _collapsedOutgoingChatId.Value = null;
        switch (lastCall.Phase) {
        case CallPhase.Ringing:
            if (_overLockRingChatId.Value == chatId) {
                ClearOverLock();
                Bridge?.MoveBehindLockScreen();
            }
            break;
        case CallPhase.Dialing:
            if (_foregroundRawChatId.Value == chatId)
                _ = Hub.Dispatcher.InvokeAsync(() => HangUpForegroundCall(chatId));
            break;
        case CallPhase.Active:
            _ = Hub.Dispatcher.InvokeAsync(() => TeardownInCall(chatId));
            break;
        }
    }

    [ComputeMethod]
    protected virtual async Task<ScreenInput> GetScreenInput(CancellationToken cancellationToken)
    {
        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        var dialingOutChatId = await CallUI.GetDialingOutChatId(cancellationToken).ConfigureAwait(false);
        return new ScreenInput(call, dialingOutChatId);
    }

    // Shows the outgoing-call UI while still dialing: the full-screen view on narrow screens, or the
    // OutgoingCallModal on wide screens.
    private void ShowOutgoingCall(ChatId chatId)
    {
        // A prior call to this same chat may have left this collapsed - without clearing it here,
        // this fresh dial would inherit that flag and jump straight to the island.
        if (_collapsedOutgoingChatId.Value == chatId)
            _collapsedOutgoingChatId.Value = null;

        if (Hub.BrowserInfo.ScreenSize.Value.IsNarrow()) {
            _foregroundRawChatId.Value = chatId;
            return;
        }

        ShowOutgoingCallModal(chatId);
    }

    // Same full-screen call view as an accepted incoming call, narrow layout only.
    private void ShowForegroundCall(ChatId chatId)
    {
        if (Hub.BrowserInfo.ScreenSize.Value.IsNarrow())
            _foregroundRawChatId.Value = chatId;
    }

    // ModalUI.Show needs the Blazor dispatcher; callers can be background loops or a UI event handler.
    private void ShowOutgoingCallModal(ChatId chatId)
        => _ = Hub.Dispatcher.InvokeAsync(() => Hub.ModalUI.Show(new OutgoingCallModal.Model(chatId)));

    private async Task SyncRings(CancellationToken cancellationToken)
    {
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
        // Skipped while the ring is over the lock screen or collapsed into the island (see GetModalCall).
        // The modal closes itself when GetModalCall drops to null; the per-chat guard stops it re-popping.
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
        CallUI.DropRing(chatId);
        ClearRingFlags(chatId);
        Bridge?.DismissCallNotification(chatId);
    }

    private void ClearRingFlags(ChatId chatId)
    {
        if (_collapsedChatId.Value == chatId)
            _collapsedChatId.Value = null;
        if (_mutedRingChatId.Value == chatId)
            _mutedRingChatId.Value = null;
    }

    private void ClearOverLock()
        => _overLockRingChatId.Value = null;

    private void ClearForegroundCall(ChatId chatId)
    {
        if (_foregroundRawChatId.Value == chatId)
            _foregroundRawChatId.Value = null;
    }

    // Stops local audio, with no screen-specific side effect - the desktop plain-chat view has no call
    // screen to close, so this is all it needs on hang-up. Leaving the call server-side follows from
    // this via the same SetParticipation path as any other presence change - see RunParticipationSync.
    private async Task HangUpQuietly(ChatId chatId)
    {
        CallUI.Release(chatId);
        await StopCallAudio(chatId).ConfigureAwait(true);
    }

    private async Task StopCallAudio(ChatId chatId)
    {
        await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(true);
        await ChatAudioUI.SetListeningState(chatId, false).ConfigureAwait(true);
    }

    // Nested types

    protected sealed record ScreenInput(ActiveCall? Call, ChatId? DialingOutChatId);
}

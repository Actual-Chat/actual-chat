using ActualChat.Live;
using ActualChat.Localization;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Everything that shows the call <see cref="CallUI"/> holds: the modal, the island, the full-screen view,
/// the ringtone and the ringback. <see cref="GetCallView"/> alone decides which of them shows it.
/// </summary>
public partial class CallScreensUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    // Screen requests that can outlive their call: DecideView honors them only while the slot holds that call.
    private readonly MutableState<ChatId?> _overLockRingChatId;
    // Outlives the phase it was set in: a collapsed dial stays in the island once the peer answers.
    private readonly MutableState<ChatId?> _collapsedChatId;
    // The ring that must not sound while it keeps going: silenced by the user, or already answered.
    private readonly MutableState<ChatId?> _mutedRingChatId;
    private int _overLockRingGeneration;

    public IState<ChatId?> MutedRingChatId => _mutedRingChatId;

    private IIncomingCallsBridge? Bridge { get; }
    private CallUI CallUI => Hub.CallUI;
    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;
    private ILogger? CallDebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.AndroidIncomingCalls);

    public CallScreensUI(AppUIHub hub) : base(hub)
    {
        Bridge = hub.Services.GetService<IIncomingCallsBridge>();
        _overLockRingChatId = NewChatIdState("OverLockRingChatId");
        _collapsedChatId = NewChatIdState("CollapsedChatId");
        _mutedRingChatId = NewChatIdState("MutedRingChatId");
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual async Task<CallView> GetCallView(CancellationToken cancellationToken)
    {
        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        if (call is null)
            return CallView.None;

        var screenSize = await Hub.BrowserInfo.ScreenSize.Use(cancellationToken).ConfigureAwait(false);
        var flags = new CallScreenFlags(
            await _collapsedChatId.Use(cancellationToken).ConfigureAwait(false),
            await _overLockRingChatId.Use(cancellationToken).ConfigureAwait(false));
        return DecideView(call, screenSize.IsNarrow(), flags);
    }

    [ComputeMethod]
    public virtual async Task<CallScreenState?> GetScreen(CancellationToken cancellationToken)
    {
        var view = await GetCallView(cancellationToken).ConfigureAwait(false);
        var watchingChatId = await ChatVideoUI.GetWatchingChatId(cancellationToken).ConfigureAwait(false);
        var watchingMode = watchingChatId is { } chatId
            ? await Hub.ChatActivityUI.GetPanelMode(chatId, cancellationToken).ConfigureAwait(false)
            : VisualActivityPanelMode.Inline;
        return DecideScreen(view, watchingChatId, watchingMode);
    }

    [ComputeMethod]
    public virtual async Task<AuthorId?> GetCallPeerId(CancellationToken cancellationToken)
    {
        // The caller of a ring; for my own call, whoever I'm calling.
        // A ring carries its caller, and a call placed to one invitee carries them from the gesture; a call
        // to several has no one peer to show.
        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        return call?.PeerId;
    }

    // The equal grid is a layout of the full screen: inline and floating, the video keeps its speaker view
    [ComputeMethod]
    public virtual async Task<bool> IsEqualLayout(CancellationToken cancellationToken)
    {
        var screen = await GetScreen(cancellationToken).ConfigureAwait(false);
        return screen is { Mode: VisualActivityPanelMode.Expanded }
            && await ChatVideoUI.GetIsVideoPanelEqualLayout(cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<Moment?> GetCallJoinedAt(ChatId chatId, CancellationToken cancellationToken)
    {
        // A call's timer runs once the call is on; null until my own join is known, as Accept commits
        // the call before my audio starts.
        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        if (call is not { Phase: CallPhase.Active } || call.ChatId != chatId)
            return null;

        // The session itself can predate the call - a call can ring into an ongoing one - so a call timer
        // counts from my own join, not from LiveSession.StartedAt.
        var live = await Hub.LiveSessionUI.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (live is null)
            return null;

        var ownAuthor = await Hub.Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (ownAuthor is null)
            return null;

        var me = live.Members.FirstOrDefault(m => m.AuthorId == ownAuthor.Id);
        return me is null || me.JoinedAt == default ? null : me.JoinedAt;
    }

    [ComputeMethod]
    public virtual async Task<IncomingCall?> GetIncomingCall(CancellationToken cancellationToken)
    {
        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        return call is { Role: CallRole.Callee, Phase: CallPhase.Ringing, PeerId: { } callerId, CallId: { } callId }
            ? new IncomingCall(callId, callerId, call.HasVideo)
            : null;
    }

    public void OnRing(ChatId chatId, bool showOverLockScreen = false)
    {
        if (chatId.Value.IsNullOrEmpty())
            return;

        CallDebugLog?.LogInformation("CALL_TRACE: OnRing #{ChatId}, showOverLockScreen={ShowOverLockScreen}",
            chatId, showOverLockScreen);
        CallUI.Touch();
        // A ring that can't hold the slot must not take the screen over the lock: it would swap the held
        // call's screen for its own.
        var slotChatId = CallUI.GetCallChatIdNonComputed();
        if (showOverLockScreen && (slotChatId is null || slotChatId == chatId)) {
            _overLockRingChatId.Value = chatId;
            _ = ClearOverLockAfterRing(chatId, Interlocked.Increment(ref _overLockRingGeneration));
        }
    }

    public void OnCallDismissed(ChatId chatId, CallId? callId = null)
    {
        if (chatId.Value.IsNullOrEmpty())
            return;

        if (CallUI.GetActiveCallNonComputed() is { } held && held.ChatId == chatId && !held.IsCall(chatId, callId)) {
            // A dismissal that outlived its call: the chat rings, or talks, in the next one by now.
            CallDebugLog?.LogInformation(
                "CALL_TRACE: OnCallDismissed #{CallId} - stale, the slot holds #{HeldCallId}", callId, held.CallId);
            Bridge?.DismissCallNotification(chatId, callId);
            return;
        }

        EndRing(chatId, callId);
        _ = Bridge?.OnCallHandled(chatId, callId, false);
    }

    public void OnOverLockScreenRendered()
    {
        // The render callback fires before the WebView paints, so the native cover goes a beat later -
        // otherwise a cold start flashes the restored route for a frame.
        CallDebugLog?.LogInformation("CALL_TRACE: OnOverLockScreenRendered");
        _ = RevealCallScreenAfterPaint();
    }

    public async Task Accept(ChatId chatId, CallId? callId = null)
    {
        // An answer from the app's own screens names no call: it is for the one the slot shows.
        callId ??= CallUI.GetCallIdNonComputed(chatId);
        var isOverLock = _overLockRingChatId.Value == chatId;
        Log.LogInformation("Accept: call #{CallId}, overLock={IsOverLock}", callId?.Value ?? chatId.Value, isOverLock);

        // Read before the commit, which makes the slot Active either way: a stale Answer - a second tap,
        // or a notification action the user hits again - must not end the call it is already holding.
        var wasInCall = CallUI.GetActiveCallNonComputed() is { Phase: CallPhase.Active } held
            && held.IsCall(chatId, callId);

        // The answer ends the ring for the user right here, but the slot stays Ringing until the accept
        // round trip lands - and on Android the notification's own ringer has already stopped by then,
        // so a ringtone driven by the slot reads as the ring starting over.
        _mutedRingChatId.Value = chatId;

        // Committed before the ring is dropped and before the accept RPC starts: the view, derived from
        // the slot, must not blink off between "ring ended" and "audio started".
        if (!CallUI.TryCommitAccept(chatId, callId)) {
            ClearIf(_mutedRingChatId, chatId);
            ShowToast(L.Call_AlreadyInCall);
            return;
        }

        // Cleared only once committed: on a ring still in the slot, dropping collapsed would bring the modal back.
        // The ring mute is NOT cleared here - it has to outlast the accept round trip, which can put the
        // slot back to Ringing for a beat; the release teardown clears it through ClearCallFlags.
        ClearIf(_collapsedChatId, chatId);
        Bridge?.DismissCallNotification(chatId, callId);
        try {
            // No id - no ring: the slot never held one, so there is nothing the server could connect.
            if (callId is null)
                throw StandardError.Constraint("There's no ring left to accept in this chat.");

            await CallUI.AcceptCall(callId, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception e) {
            // Also where "there was no ring left" lands: the server decides that under its change
            // lock, and it's the only reading of it that can't race the call it came from.
            Log.LogWarning(e, "AcceptCall failed for chat #{ChatId}", chatId);
            if (wasInCall)
                return;

            CallUI.Release(chatId);
            _ = Bridge?.OnCallHandled(chatId, callId, false);
            // The server's refusal is a StandardError.Constraint: the ring ran out, or the caller hung up.
            ShowToast(e is InvalidOperationException ? L.Call_Entry_Missed : L.Call_Ended);
            return;
        }

        // Already on the line: this answer was the duplicate it looked like, and joining again would
        // ask for the keyguard a second time over a call that is running.
        if (wasInCall)
            return;

        try {
            await JoinAcceptedCall(chatId, callId, isOverLock).ConfigureAwait(true);
        }
        catch {
            CallUI.Release(chatId);
            throw;
        }
    }

    public async Task Decline(ChatId chatId, CallId? callId = null)
    {
        callId ??= CallUI.GetCallIdNonComputed(chatId);
        // A held over-lock ring goes back behind the lock screen in the release teardown, as a ring that ends
        // on its own does; a ring the slot never held has no release, so it goes back here.
        var isOverLock = _overLockRingChatId.Value == chatId;
        var isHeld = EndRing(chatId, callId);
        if (!isOverLock || !isHeld)
            _ = Bridge?.OnCallHandled(chatId, callId, false);
        if (callId is null)
            return; // The slot never held a ring, so there is no call to tell the server about

        try {
            await CallUI.DeclineCall(callId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "DeclineCall failed for chat #{ChatId}", chatId);
        }
    }

    public async Task DeclineAndOpenChat(ChatId chatId)
    {
        await Decline(chatId).ConfigureAwait(true);
        await OpenChat(chatId).ConfigureAwait(true);
    }

    public void ToggleMuteRing(ChatId chatId)
        => _mutedRingChatId.Value = _mutedRingChatId.Value == chatId ? null : chatId;

    public void Collapse(ChatId chatId)
    {
        // A modal disposed after its call ended must not collapse whatever holds the slot next.
        if (CallUI.GetActiveCallNonComputed() is not { } call || call.ChatId != chatId)
            return;

        // A collapsed ring rings silently; the island still accepts and declines it.
        if (call.Phase == CallPhase.Ringing)
            _mutedRingChatId.Value = chatId;
        _collapsedChatId.Value = chatId;
    }

    public void Expand(ChatId chatId)
        => SetScreenMode(chatId, VisualActivityPanelMode.Expanded);

    public void SetScreenMode(ChatId chatId, VisualActivityPanelMode mode)
    {
        // The one place the screen of a call and of its video changes mode: the collapsed flag and the
        // video's panel mode are two stores of it, and written apart they disagree.
        if (mode == VisualActivityPanelMode.Expanded)
            ClearIf(_collapsedChatId, chatId);
        else if (CallUI.GetActiveCallNonComputed() is { Phase: not CallPhase.Ringing } call && call.ChatId == chatId)
            _collapsedChatId.Value = chatId;
        if (ChatVideoUI.WatchingChatId == chatId)
            Hub.ChatActivityUI.SetPanelMode(chatId, mode);
    }

    public Task HangUp(ChatId chatId)
    {
        var call = CallUI.GetActiveCallNonComputed();
        if (call is null || call.ChatId != chatId)
            return LeaveLiveSession(chatId);

        return call is { Role: CallRole.Caller, Phase: CallPhase.Dialing }
            ? CancelCall(chatId)
            : CallUI.HangUp(chatId);
    }

    public async Task LeaveCallScreen(ChatId chatId)
    {
        if (!await LeaveLockScreen(chatId).ConfigureAwait(true))
            return;

        // The chat goes first, under the cover of the screen: an inline video belongs to its chat's
        // page, and is closed if that page isn't the one open.
        await OpenChat(chatId).ConfigureAwait(true);
        // Collapsed goes before the over-lock flag, which cleared alone would bring the narrow
        // full-screen view back.
        SetScreenMode(chatId, VisualActivityPanelMode.Inline);
        ClearIf(_overLockRingChatId, chatId);
    }

    // Private methods

    private async Task LeaveLiveSession(ChatId chatId)
    {
        // Video outside a call: there is no slot to release, only this chat's media to stop.
        ChatVideoUI.LeaveVideoSession(chatId);
        var chatAudioUI = Hub.ChatAudioUI;
        if (await chatAudioUI.GetRecordingChatId().ConfigureAwait(true) == chatId)
            await chatAudioUI.SetRecordingChatId(null).ConfigureAwait(true);
        await chatAudioUI.SetListeningState(chatId, false).ConfigureAwait(true);
    }

    private async Task<bool> LeaveLockScreen(ChatId chatId)
    {
        // The chat is behind the keyguard; a cancelled PIN keeps the call screen up.
        if (_overLockRingChatId.Value != chatId || Bridge is null)
            return true;

        return await Bridge.OnCallHandled(chatId, CallUI.GetCallIdNonComputed(chatId), true).ConfigureAwait(true);
    }

    private async Task ClearOverLockAfterRing(ChatId chatId, int generation)
    {
        // No unanswered ring outlives RingTimeout, so past it only a held call keeps the flag.
        try {
            await Task.Delay(Constants.Call.RingTimeout, StopToken).ConfigureAwait(false);
            var isSameRing = Volatile.Read(ref _overLockRingGeneration) == generation;
            var heldChatId = CallUI.GetCallChatIdNonComputed();
            if (IsOverLockFlagStale(_overLockRingChatId.Value, chatId, isSameRing, heldChatId))
                ClearIf(_overLockRingChatId, chatId);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Clearing the over-lock flag of the ring in chat #{ChatId} failed", chatId);
        }
    }

    private async Task RevealCallScreenAfterPaint()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
        CallDebugLog?.LogInformation("CALL_TRACE: RevealCallScreen (after paint delay), Bridge={HasBridge}",
            Bridge is not null);
        Bridge?.RevealCallScreen();
    }

    private async Task JoinAcceptedCall(ChatId chatId, CallId? callId, bool isOverLock)
    {
        // Over the lock screen the activity shown via SetShowWhenLocked counts as foreground, so the mic FGS
        // starts without unlocking; anywhere else the keyguard goes first, as the FGS can't start from the
        // background. The chat opens under the call; over the lock screen it waits for the user to unlock.
        var canStartAudio = isOverLock
            || Bridge is null
            || await Bridge.OnCallHandled(chatId, callId, true).ConfigureAwait(true);
        Log.LogInformation("Accept: chat #{ChatId}, canStartAudio={CanStartAudio}", chatId, canStartAudio);
        if (!isOverLock)
            await OpenChat(chatId).ConfigureAwait(true);
        if (canStartAudio)
            await CallUI.StartCallAudio(chatId, CancellationToken.None).ConfigureAwait(true);
    }

    private async Task CancelCall(ChatId chatId)
    {
        try {
            await CallUI.CancelCall(chatId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "CancelCall failed for chat #{ChatId}", chatId);
        }
    }

    private bool EndRing(ChatId chatId, CallId? callId)
    {
        // A ring the slot never held (e.g. declined before the search claimed it) has no release to clear
        // its flags, so they go here - unless the slot holds another call to this chat, whose flags they are.
        var isHeld = CallUI.DropRing(chatId, callId);
        Bridge?.DismissCallNotification(chatId, callId);
        if (!isHeld && CallUI.GetCallChatIdNonComputed() != chatId)
            ClearCallFlags(chatId);
        return isHeld;
    }

    private async Task OpenChat(ChatId chatId)
    {
        // The panels hide on a URL change, so the chat they were opened over has to hide them itself
        var isChatOpen = Hub.History.LocalUrl.IsChat(out var openChatId) && openChatId == chatId;
        await Hub.History.NavigateTo(Links.Chat(chatId)).ConfigureAwait(true);
        if (isChatOpen)
            Hub.PanelsUI.HidePanels();
    }

    private void ShowToast(string text)
        => Hub.ToastUI.Show(text, "icon-phone", ToastDismissDelay.Short);

    private void ShowModal<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TModel>(
        TModel model, CancellationToken cancellationToken = default)
        where TModel : class
        // ModalUI.Show needs the Blazor dispatcher, and the worker loops run off it.
        => _ = Hub.Dispatcher.InvokeAsync(() => Hub.ModalUI.Show(model, cancellationToken));

    private MutableState<ChatId?> NewChatIdState(string name)
        => StateFactory.NewMutable((ChatId?)null, StateCategories.Get(GetType(), name));

    private static void ClearIf(MutableState<ChatId?> state, ChatId chatId)
    {
        if (state.Value == chatId)
            state.Value = null;
    }
}

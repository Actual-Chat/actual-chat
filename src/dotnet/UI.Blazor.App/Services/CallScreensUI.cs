using ActualChat.Live;
using ActualChat.Localization;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Everything that shows the call <see cref="CallUI"/> holds: the modal, the island, the full-screen view,
/// the ringtone. <see cref="GetCallView"/> alone decides which of them shows it.
/// </summary>
public partial class CallScreensUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    // Screen requests that can outlive their call: DecideView honors them only while the slot holds that call.
    private readonly MutableState<ChatId?> _overLockRingChatId;
    private readonly MutableState<ChatId?> _collapsedChatId;
    // The active call whose full-screen view gave way to its chat.
    private readonly MutableState<ChatId?> _inChatChatId;
    // The ring that must not sound while it keeps going: silenced by the user, or already answered.
    private readonly MutableState<ChatId?> _mutedRingChatId;
    private int _overLockRingGeneration;

    public IState<ChatId?> MutedRingChatId => _mutedRingChatId;

    private IIncomingCallsBridge? Bridge { get; }
    private CallUI CallUI => Hub.CallUI;
    private ILogger? CallDebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.AndroidIncomingCalls);

    public CallScreensUI(AppUIHub hub) : base(hub)
    {
        Bridge = hub.Services.GetService<IIncomingCallsBridge>();
        _overLockRingChatId = NewChatIdState("OverLockRingChatId");
        _collapsedChatId = NewChatIdState("CollapsedChatId");
        _inChatChatId = NewChatIdState("InChatChatId");
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

        var dialingOutChatId = await CallUI.GetDialingOutChatId(cancellationToken).ConfigureAwait(false);
        var screenSize = await Hub.BrowserInfo.ScreenSize.Use(cancellationToken).ConfigureAwait(false);
        var flags = new CallScreenFlags(
            await _collapsedChatId.Use(cancellationToken).ConfigureAwait(false),
            await _inChatChatId.Use(cancellationToken).ConfigureAwait(false),
            await _overLockRingChatId.Use(cancellationToken).ConfigureAwait(false));
        return DecideView(call, dialingOutChatId == call.ChatId, screenSize.IsNarrow(), flags);
    }

    [ComputeMethod]
    public virtual async Task<AuthorId?> GetCallPeerId(CancellationToken cancellationToken)
    {
        // The caller of a ring; for my own call, whoever I'm calling.
        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        if (call is null)
            return null;
        if (call.Role == CallRole.Callee)
            return call.PeerId;

        var live = await Hub.LiveSessionUI.Get(call.ChatId, cancellationToken).ConfigureAwait(false);
        return live is { Invites.Count: > 0 } ? live.Invites[0].InviteeId : null;
    }

    [ComputeMethod]
    public virtual async Task<IncomingCall?> GetIncomingCall(CancellationToken cancellationToken)
    {
        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        return call is { Role: CallRole.Callee, Phase: CallPhase.Ringing, PeerId: { } callerId }
            ? new IncomingCall(call.ChatId, callerId, call.HasVideo)
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

    public void OnCallDismissed(ChatId chatId)
    {
        if (chatId.Value.IsNullOrEmpty())
            return;

        EndRing(chatId);
        _ = Bridge?.OnCallHandled(chatId, false);
    }

    public void OnOverLockScreenRendered()
    {
        // The render callback fires before the WebView paints, so the native cover goes a beat later -
        // otherwise a cold start flashes the restored route for a frame.
        CallDebugLog?.LogInformation("CALL_TRACE: OnOverLockScreenRendered");
        _ = RevealCallScreenAfterPaint();
    }

    public async Task Accept(ChatId chatId)
    {
        var isOverLock = _overLockRingChatId.Value == chatId;
        Log.LogInformation("Accept: chat #{ChatId}, overLock={IsOverLock}", chatId, isOverLock);

        // The answer ends the ring for the user right here, but the slot stays Ringing until the accept
        // round trip lands - and on Android the notification's own ringer has already stopped by then,
        // so a ringtone driven by the slot reads as the ring starting over.
        _mutedRingChatId.Value = chatId;

        // Committed before the ring is dropped and before the accept RPC starts: the view, derived from
        // the slot, must not blink off between "ring ended" and "audio started".
        if (!CallUI.TryCommitAccept(chatId)) {
            ClearIf(_mutedRingChatId, chatId);
            ShowToast(L.Call_AlreadyInCall);
            return;
        }

        // Cleared only once committed: on a ring still in the slot, dropping collapsed would bring the modal back.
        ClearIf(_collapsedChatId, chatId);
        ClearIf(_mutedRingChatId, chatId);
        Bridge?.DismissCallNotification(chatId);
        try {
            await CallUI.AcceptCall(chatId, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception e) {
            // Also where "there was no ring left" lands: the server decides that under its change
            // lock, and it's the only reading of it that can't race the session it came from.
            CallUI.Release(chatId);
            _ = Bridge?.OnCallHandled(chatId, false);
            Log.LogWarning(e, "AcceptCall failed for chat #{ChatId}", chatId);
            ShowToast(L.Call_Ended);
            return;
        }

        try {
            await JoinAcceptedCall(chatId, isOverLock).ConfigureAwait(true);
        }
        catch {
            CallUI.Release(chatId);
            throw;
        }
    }

    public async Task Decline(ChatId chatId)
    {
        // A held over-lock ring goes back behind the lock screen in the release teardown, as a ring that ends
        // on its own does; a ring the slot never held has no release, so it goes back here.
        var isOverLock = _overLockRingChatId.Value == chatId;
        var isHeld = EndRing(chatId);
        if (!isOverLock || !isHeld)
            _ = Bridge?.OnCallHandled(chatId, false);
        try {
            await CallUI.DeclineCall(chatId, CancellationToken.None).ConfigureAwait(false);
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
        => ClearIf(_collapsedChatId, chatId);

    public Task HangUp(ChatId chatId)
        => CallUI.GetActiveCallNonComputed() is { Role: CallRole.Caller, Phase: CallPhase.Dialing } call
            && call.ChatId == chatId
            ? CancelCall(chatId)
            : CallUI.HangUp(chatId);

    public async Task LeaveCallScreen(ChatId chatId)
    {
        if (_overLockRingChatId.Value == chatId) {
            // The chat is behind the keyguard; a cancelled PIN keeps the call screen up.
            var isUnlocked = Bridge is null || await Bridge.OnCallHandled(chatId, true).ConfigureAwait(true);
            if (!isUnlocked)
                return;
        }

        // In chat goes first: the over-lock flag cleared alone would bring the narrow full-screen view back.
        _inChatChatId.Value = chatId;
        ClearIf(_overLockRingChatId, chatId);
        await OpenChat(chatId).ConfigureAwait(true);
    }

    // Private methods

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

    private async Task JoinAcceptedCall(ChatId chatId, bool isOverLock)
    {
        // Over the lock screen the activity shown via SetShowWhenLocked counts as foreground, so the mic FGS
        // starts without unlocking; anywhere else the keyguard goes first, as the FGS can't start from the
        // background. The chat opens under the call; over the lock screen it waits for the user to unlock.
        var canStartAudio = isOverLock
            || Bridge is null
            || await Bridge.OnCallHandled(chatId, true).ConfigureAwait(true);
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

    private bool EndRing(ChatId chatId)
    {
        // A ring the slot never held (e.g. declined before the search claimed it) has no release to clear
        // its flags, so they go here.
        var isHeld = CallUI.DropRing(chatId);
        Bridge?.DismissCallNotification(chatId);
        if (!isHeld)
            ClearCallFlags(chatId);
        return isHeld;
    }

    private Task OpenChat(ChatId chatId)
        => Hub.History.NavigateTo(Links.Chat(chatId));

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

using ActualChat.Localization;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Everything that shows the call <see cref="CallUI"/> holds: the incoming and outgoing modals and their
/// islands, the over-lock and narrow full-screen views, the ringtone. The call mechanics live in <see cref="CallUI"/>.
/// </summary>
public partial class CallScreensUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    // Screen requests that can outlive their call: GetOverLockChatId, GetForegroundCallChatId and IsOverLock
    // honor them only while the slot holds that call.
    private readonly MutableState<ChatId?> _overLockRingChatId;
    private readonly MutableState<ChatId?> _foregroundCallChatId;
    private readonly MutableState<ChatId?> _collapsedIncomingChatId;
    private readonly MutableState<ChatId?> _collapsedOutgoingChatId;
    // The ring whose ringtone the user silenced; the ring itself keeps going.
    private readonly MutableState<ChatId?> _mutedRingChatId;

    public IState<ChatId?> CollapsedIncomingChatId => _collapsedIncomingChatId;
    public IState<ChatId?> CollapsedOutgoingChatId => _collapsedOutgoingChatId;
    public IState<ChatId?> MutedRingChatId => _mutedRingChatId;

    private IIncomingCallsBridge? Bridge { get; }
    private CallUI CallUI => Hub.CallUI;
    private bool IsNarrowScreen => Hub.BrowserInfo.ScreenSize.Value.IsNarrow();
    private ILogger? CallDebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.AndroidIncomingCalls);

    public CallScreensUI(AppUIHub hub) : base(hub)
    {
        Bridge = hub.Services.GetService<IIncomingCallsBridge>();
        _overLockRingChatId = NewChatIdState("OverLockRingChatId");
        _foregroundCallChatId = NewChatIdState("ForegroundCallChatId");
        _collapsedIncomingChatId = NewChatIdState("CollapsedIncomingChatId");
        _collapsedOutgoingChatId = NewChatIdState("CollapsedOutgoingChatId");
        _mutedRingChatId = NewChatIdState("MutedRingChatId");
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual async Task<IncomingCall?> GetIncomingCall(CancellationToken cancellationToken)
    {
        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        return call is { Origin: CallOrigin.Incoming, Phase: CallPhase.Ringing, PeerId: { } callerId }
            ? new IncomingCall(call.ChatId, callerId, call.HasVideo)
            : null;
    }

    [ComputeMethod]
    public virtual async Task<ChatId?> GetOverLockChatId(CancellationToken cancellationToken)
    {
        var chatId = await _overLockRingChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return null;

        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        return call is { Origin: CallOrigin.Incoming } && call.ChatId == chatId ? chatId : null;
    }

    [ComputeMethod]
    public virtual async Task<ChatId?> GetForegroundCallChatId(CancellationToken cancellationToken)
    {
        var chatId = await _foregroundCallChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return null;

        var callChatId = await CallUI.GetCallChatId(cancellationToken).ConfigureAwait(false);
        return callChatId == chatId ? chatId : null;
    }

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

    public void OnCallDismissed(ChatId chatId)
    {
        if (chatId.Value.IsNullOrEmpty())
            return;

        EndRing(chatId);
        _ = Bridge?.OnCallHandled(false);
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
        // Straight from the session, not the slot: Answer on an Android notification can land before the
        // search claimed the ring.
        var call = await CallUI.GetRingingCall(chatId, CancellationToken.None).ConfigureAwait(true);
        if (call is null) {
            EndRing(chatId);
            _ = Bridge?.OnCallHandled(false);
            ShowToast(L.Call_Ended);
            return;
        }

        // Committed before the ring is dropped and before the accept RPC starts: screen visibility, derived
        // from the slot, must not blink off between "ring ended" and "audio started".
        if (!CallUI.TryCommitAccept(call)) {
            ShowToast(L.Call_AlreadyInCall);
            return;
        }

        ClearRingFlags(chatId);
        Bridge?.DismissCallNotification(chatId);
        try {
            await CallUI.AcceptCall(chatId, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception e) {
            CallUI.Release(chatId);
            _ = Bridge?.OnCallHandled(false);
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
        var isOverLock = _overLockRingChatId.Value == chatId;
        _overLockRingChatId.Value = null;
        EndRing(chatId);
        if (isOverLock)
            Bridge?.MoveBehindLockScreen();
        else
            _ = Bridge?.OnCallHandled(false);
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

    public void CollapseIncoming(ChatId chatId)
    {
        // The island rings silently; its Accept and Decline still work, and a tap on it brings the modal back.
        _mutedRingChatId.Value = chatId;
        _collapsedIncomingChatId.Value = chatId;
    }

    public void ExpandIncoming(ChatId chatId)
        => ClearIf(_collapsedIncomingChatId, chatId);

    public void CollapseOutgoing(ChatId chatId)
        => _collapsedOutgoingChatId.Value = chatId;

    public void ExpandOutgoing(ChatId chatId)
    {
        if (_collapsedOutgoingChatId.Value != chatId)
            return;

        // Collapsing closed the modal for good, so expanding shows a new one; if dialing has ended
        // meanwhile, that modal closes itself right away.
        _collapsedOutgoingChatId.Value = null;
        ShowModal(new OutgoingCallModal.Model(chatId));
    }

    public Task HangUp(ChatId chatId)
    {
        var call = CallUI.GetActiveCallNonComputed();
        return call is { Origin: CallOrigin.Outgoing, Phase: CallPhase.Dialing } && call.ChatId == chatId
            ? CancelDialing(chatId)
            : CloseCall(chatId, call);
    }

    public async Task LeaveCallScreen(ChatId chatId)
    {
        if (IsOverLock(chatId, CallUI.GetActiveCallNonComputed())) {
            // The chat is behind the keyguard; a cancelled PIN keeps the call screen up.
            var isUnlocked = Bridge is null || await Bridge.OnCallHandled(true).ConfigureAwait(true);
            if (!isUnlocked)
                return;

            _overLockRingChatId.Value = null;
        }
        else
            ClearIf(_foregroundCallChatId, chatId);
        await OpenChat(chatId).ConfigureAwait(true);
    }

    // Private methods

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
        // background. A narrow screen gets the full-screen call view, which opens the chat once it closes.
        var canStartAudio = isOverLock || Bridge is null || await Bridge.OnCallHandled(true).ConfigureAwait(true);
        var showsForegroundCall = !isOverLock && IsNarrowScreen;
        if (!isOverLock && !showsForegroundCall)
            await OpenChat(chatId).ConfigureAwait(true);
        if (canStartAudio)
            await CallUI.StartCallAudio(chatId, CancellationToken.None).ConfigureAwait(true);
        if (showsForegroundCall)
            _foregroundCallChatId.Value = chatId;
    }

    private async Task CancelDialing(ChatId chatId)
    {
        ClearIf(_foregroundCallChatId, chatId);
        try {
            await CallUI.CancelCall(chatId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "CancelCall failed for chat #{ChatId}", chatId);
        }
    }

    private async Task CloseCall(ChatId chatId, ActiveCall? call)
    {
        // Over the lock screen the app goes back behind it; a foreground call screen gives way to the chat.
        if (IsOverLock(chatId, call)) {
            _overLockRingChatId.Value = null;
            Bridge?.MoveBehindLockScreen();
            await CallUI.HangUp(chatId).ConfigureAwait(true);
            return;
        }

        var hasForegroundScreen = _foregroundCallChatId.Value == chatId;
        if (hasForegroundScreen)
            _foregroundCallChatId.Value = null;
        await CallUI.HangUp(chatId).ConfigureAwait(true);
        if (hasForegroundScreen)
            await OpenChat(chatId).ConfigureAwait(true);
    }

    private void EndRing(ChatId chatId)
    {
        CallUI.DropRing(chatId);
        ClearRingFlags(chatId);
        Bridge?.DismissCallNotification(chatId);
    }

    private void ClearRingFlags(ChatId chatId)
    {
        ClearIf(_collapsedIncomingChatId, chatId);
        ClearIf(_mutedRingChatId, chatId);
    }

    private bool IsOverLock(ChatId chatId, ActiveCall? call)
        => _overLockRingChatId.Value == chatId && call is { Origin: CallOrigin.Incoming } && call.ChatId == chatId;

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

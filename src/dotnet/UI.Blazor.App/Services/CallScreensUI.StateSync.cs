namespace ActualChat.UI.Blazor.App.Services;

public partial class CallScreensUI
{
    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(SyncRingtone),
            AsyncChain.From(SyncCallView),
        };
        var retryDelays = RetryDelaySeq.Exp(0.5, 10);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).Run(cancellationToken);
    }

    [ComputeMethod]
    protected virtual async Task<bool> MustRing(CancellationToken cancellationToken)
    {
        var call = await GetIncomingCall(cancellationToken).ConfigureAwait(false);
        if (call is null)
            return false;

        var mutedChatId = await _mutedRingChatId.Use(cancellationToken).ConfigureAwait(false);
        return mutedChatId != call.ChatId;
    }

    // Private methods

    private async Task SyncRingtone(CancellationToken cancellationToken)
    {
        var cMustRing = await Computed
            .Capture(() => MustRing(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var isRinging = false;
        try {
            await foreach (var c in cMustRing.Changes(cancellationToken).ConfigureAwait(false)) {
                if (c.Value == isRinging)
                    continue;

                isRinging = c.Value;
                if (isRinging)
                    StartRinging();
                else
                    StopRinging();
            }
        }
        finally {
            if (isRinging)
                StopRinging();
        }
    }

    private async Task SyncCallView(CancellationToken cancellationToken)
    {
        var cView = await Computed
            .Capture(() => GetCallView(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var last = CallView.None;
        await foreach (var c in cView.Changes(cancellationToken).ConfigureAwait(false)) {
            var view = c.Value;
            if (view == last)
                continue;

            OnCallViewChanged(last, view);
            last = view;
        }
    }

    private void OnCallViewChanged(CallView last, CallView view)
    {
        if (last.Call is { } lastCall && lastCall.ChatId != view.Call?.ChatId)
            OnCallReleased(lastCall, last);
        if (view is not { Kind: CallViewKind.Modal, Call: { } call })
            return;

        // The modal closes itself once the view moves on, so it opens only on the switch to it.
        var wasModal = last.Kind == CallViewKind.Modal && last.Call?.ChatId == call.ChatId;
        if (!wasModal)
            ShowCallModal(call);
    }

    private void OnCallReleased(ActiveCall call, CallView last)
    {
        // The one place a call's screens are torn down, however the call ended.
        var chatId = call.ChatId;
        CallDebugLog?.LogInformation("CALL_TRACE: slot released #{ChatId} from {Phase}", chatId, call.Phase);
        ClearCallFlags(chatId);
        if (last.IsOverLock)
            Bridge?.MoveBehindLockScreen();
        var mustOpenChat = last is { Kind: CallViewKind.FullScreen, IsOverLock: false };
        if (call.Phase == CallPhase.Active || mustOpenChat)
            _ = Hub.Dispatcher.InvokeAsync(() => CloseCall(call, mustOpenChat));
    }

    private async Task CloseCall(ActiveCall call, bool mustOpenChat)
    {
        if (call.Phase == CallPhase.Active)
            await CallUI.HangUp(call.ChatId).ConfigureAwait(true);
        if (mustOpenChat)
            await OpenChat(call.ChatId).ConfigureAwait(true);
    }

    private void ShowCallModal(ActiveCall call)
    {
        if (call.Origin == CallOrigin.Outgoing)
            ShowModal(new OutgoingCallModal.Model(call.ChatId));
        else if (call.PeerId is { } callerId)
            ShowModal(new IncomingCallModal.Model(callerId));
    }

    private void ClearCallFlags(ChatId chatId)
    {
        ClearIf(_collapsedChatId, chatId);
        ClearIf(_inChatChatId, chatId);
        ClearIf(_overLockRingChatId, chatId);
        ClearIf(_mutedRingChatId, chatId);
    }
}

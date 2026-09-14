namespace ActualChat.UI.Blazor.App.Services;

public partial class CallScreensUI
{
    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(SyncRingtone),
            AsyncChain.From(SyncIncomingCallModal),
            AsyncChain.From(SyncCallScreens),
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

    [ComputeMethod]
    protected virtual async Task<IncomingCall?> GetModalCall(CancellationToken cancellationToken)
    {
        // No modal while the ring is shown over the lock screen or collapsed into the island.
        var call = await GetIncomingCall(cancellationToken).ConfigureAwait(false);
        if (call is null)
            return null;

        var overLockChatId = await GetOverLockChatId(cancellationToken).ConfigureAwait(false);
        var collapsedChatId = await _collapsedChatId.Use(cancellationToken).ConfigureAwait(false);
        return overLockChatId == call.ChatId || collapsedChatId == call.ChatId ? null : call;
    }

    [ComputeMethod]
    protected virtual async Task<ScreenInput> GetScreenInput(CancellationToken cancellationToken)
    {
        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        var dialingOutChatId = await CallUI.GetDialingOutChatId(cancellationToken).ConfigureAwait(false);
        return new ScreenInput(call, dialingOutChatId);
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

    private async Task SyncIncomingCallModal(CancellationToken cancellationToken)
    {
        // The modal closes itself once GetModalCall drops to null; shownChatId keeps it from re-popping.
        var cCall = await Computed
            .Capture(() => GetModalCall(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        ChatId? shownChatId = null;
        await foreach (var c in cCall.Changes(cancellationToken).ConfigureAwait(false)) {
            var call = c.Value;
            if (call is null)
                shownChatId = null;
            else if (shownChatId != call.ChatId) {
                shownChatId = call.ChatId;
                ShowModal(new IncomingCallModal.Model(call.Caller), cancellationToken);
            }
        }
    }

    private async Task SyncCallScreens(CancellationToken cancellationToken)
    {
        var cInput = await Computed
            .Capture(() => GetScreenInput(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var last = new ScreenInput(null, null);
        await foreach (var c in cInput.Changes(cancellationToken).ConfigureAwait(false)) {
            var input = c.Value;
            if (input == last)
                continue;

            OnScreenInputChanged(last, input);
            last = input;
        }
    }

    private void OnScreenInputChanged(ScreenInput last, ScreenInput input)
    {
        if (input.DialingOutChatId is { } dialingOutChatId && dialingOutChatId != last.DialingOutChatId)
            ShowOutgoingCall(dialingOutChatId);

        var (lastCall, call) = (last.Call, input.Call);
        // An answered outgoing call gets the same full-screen view as an accepted incoming one.
        if (call is { Origin: CallOrigin.Outgoing, Phase: CallPhase.Active }
            && lastCall is { Phase: CallPhase.Dialing }
            && lastCall.ChatId == call.ChatId
            && IsNarrowScreen)
            _foregroundCallChatId.Value = call.ChatId;
        if (lastCall is not null && lastCall.ChatId != call?.ChatId)
            OnCallReleased(lastCall);
    }

    private void OnCallReleased(ActiveCall call)
    {
        var chatId = call.ChatId;
        CallDebugLog?.LogInformation("CALL_TRACE: slot released #{ChatId} from {Phase}", chatId, call.Phase);
        ClearCallFlags(chatId);
        switch (call.Phase) {
        case CallPhase.Ringing:
            if (IsOverLock(chatId, call)) {
                _overLockRingChatId.Value = null;
                Bridge?.MoveBehindLockScreen();
            }
            break;
        case CallPhase.Dialing:
            // On a wide screen dialing is shown by OutgoingCallModal, which closes itself.
            if (_foregroundCallChatId.Value == chatId)
                _ = Hub.Dispatcher.InvokeAsync(() => CloseCall(chatId, call));
            break;
        case CallPhase.Active:
            _ = Hub.Dispatcher.InvokeAsync(() => CloseCall(chatId, call));
            break;
        }
    }

    private void ShowOutgoingCall(ChatId chatId)
    {
        // A prior call to this chat may have left it collapsed, and a fresh dial must not start in the island.
        ClearIf(_collapsedChatId, chatId);
        if (IsNarrowScreen)
            _foregroundCallChatId.Value = chatId;
        else
            ShowModal(new OutgoingCallModal.Model(chatId));
    }

    // Nested types

    protected sealed record ScreenInput(ActiveCall? Call, ChatId? DialingOutChatId);
}

using ActualChat.Live;
using ActualChat.Notifications;

namespace ActualChat.UI.Blazor.App.Services;

public partial class CallUI
{
    // Give up on an outgoing call whose session never shows up as dialing.
    private static readonly TimeSpan DialingWaitTimeout = TimeSpan.FromSeconds(15);

    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(HoldCalls),
            AsyncChain.From(SearchRings),
            AsyncChain.From(SyncActiveCallNotifications),
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
    protected virtual async Task<HoldingInput> GetHoldingInput(ChatId chatId, CancellationToken cancellationToken)
    {
        var slotChatId = await _callChatId.Use(cancellationToken).ConfigureAwait(false);
        var call = await _activeCall.Use(cancellationToken).ConfigureAwait(false);
        var ring = await GetRingingCall(chatId, cancellationToken).ConfigureAwait(false);
        var live = await LiveSessionUI.Get(chatId, cancellationToken).ConfigureAwait(false);
        var session = live switch {
            { Kind: LiveSessionKind.Call, Conversation: null } => CallSessionState.Dialing,
            { Kind: LiveSessionKind.Call } => CallSessionState.Connected,
            _ => CallSessionState.None,
        };
        var isInConversation = await LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken)
            .ConfigureAwait(false);
        return new HoldingInput(slotChatId, call, new CallFacts(ring, session, isInConversation));
    }

    [ComputeMethod]
    protected virtual async Task<SearchInput> GetSearchInput(CancellationToken cancellationToken)
    {
        var chatIds = await _ringingChatIds.Use(cancellationToken).ConfigureAwait(false);
        // Read only to wake the search when the slot moves: ApplySearch decides against the live slot.
        await _callChatId.Use(cancellationToken).ConfigureAwait(false);
        await _activeCall.Use(cancellationToken).ConfigureAwait(false);
        var rings = ImmutableList.CreateBuilder<IncomingCall>();
        for (var i = chatIds.Count - 1; i >= 0; i--)
            if (await GetRingingCall(chatIds[i], cancellationToken).ConfigureAwait(false) is { } ring)
                rings.Add(ring);
        return new SearchInput(chatIds, rings.ToImmutable());
    }

    // Private methods

    private async Task HoldCalls(CancellationToken cancellationToken)
    {
        var cChatId = await Computed
            .Capture(() => GetCallChatId(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested) {
            cChatId = await cChatId.When(chatId => chatId is not null, cancellationToken).ConfigureAwait(false);
            if (cChatId.Value is { } chatId)
                await Hold(chatId, cancellationToken).ConfigureAwait(false);
            cChatId = await cChatId.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task Hold(ChatId chatId, CancellationToken cancellationToken)
    {
        var memory = default(HoldingMemory);
        var dialingDeadline = Now + DialingWaitTimeout;
        var cInput = await Computed
            .Capture(() => GetHoldingInput(chatId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (true) {
            var input = cInput.Value;
            if (input.SlotChatId != chatId)
                return;

            memory = memory.Observe(input.Facts) with { IsDialingWaitOver = Now >= dialingDeadline };
            var action = DecideHolding(input.Call, input.Facts, memory);
            CallDebugLog?.LogInformation("CALL_TRACE: Hold #{ChatId} {Origin}/{Phase} → {Action}",
                chatId, input.Call?.Origin, input.Call?.Phase, action);
            switch (action) {
            case HoldingAction.Confirm:
                TryConfirm(input.Facts.Ring!);
                break;
            case HoldingAction.Join:
                if (TryCommitActive(chatId))
                    _ = StartAnsweredCallAudio(chatId, cancellationToken);
                break;
            case HoldingAction.Release:
                Release(chatId);
                return;
            }

            if (input.Call is { Phase: CallPhase.Dialing } && !memory.HasSeenDialing) {
                // A session that never shows up as dialing must still time out, with nothing to invalidate it.
                using var cts = cancellationToken.CreateLinkedTokenSource();
                var remaining = dialingDeadline - Now;
                if (remaining < TimeSpan.Zero)
                    remaining = TimeSpan.Zero;
                await Task.WhenAny(
                        cInput.WhenInvalidated(cts.Token),
                        Clocks.CpuClock.Delay(remaining, cts.Token))
                    .ConfigureAwait(false);
                cts.CancelAndDisposeSilently();
            }
            else
                await cInput.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cInput = await cInput.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private void TryConfirm(IncomingCall ring)
    {
        var chatId = ring.ChatId;
        lock (_lock) {
            if (_callChatId.Value != chatId || _activeCall.Value is not null)
                return;

            _activeCall.Value = new ActiveCall(
                chatId, CallOrigin.Incoming, CallPhase.Ringing, ring.Caller, ring.HasVideo);
        }
        _ = ConfirmRing(chatId, RingAck.Ringing);
    }

    private bool TryCommitActive(ChatId chatId)
    {
        lock (_lock) {
            if (_callChatId.Value != chatId || _activeCall.Value is not { } call)
                return false;

            _activeCall.Value = call with { Phase = CallPhase.Active };
            return true;
        }
    }

    private async Task StartAnsweredCallAudio(ChatId chatId, CancellationToken cancellationToken)
    {
        // Placing a call is itself the intent to talk, so answering it puts the caller on the line.
        // A denied mic still joins them - listening only, same as anywhere else.
        try {
            await ChatAudioUI.SetListeningState(chatId, true).ConfigureAwait(false);
            var hasMic = await Hub.AudioRecorder.MicrophonePermission
                .CheckOrRequest(cancellationToken)
                .ConfigureAwait(false);
            if (hasMic)
                await ChatAudioUI.SetRecordingChatId(chatId).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Couldn't join the answered call in chat #{ChatId}", chatId);
            Release(chatId);
        }
    }

    private async Task SearchRings(CancellationToken cancellationToken)
    {
        if (Bridge is not null) {
            // A call push may have landed while the app was killed and the user opened it
            // from the launcher - pick the ring up from the still-active system notification.
            foreach (var chatId in await Bridge.ListActiveCallChatIds(cancellationToken).ConfigureAwait(false))
                AddCandidate(chatId);
        }

        var cInput = await Computed
            .Capture(() => GetSearchInput(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested) {
            ApplySearch(cInput.Value);
            await cInput.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cInput = await cInput.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private void ApplySearch(SearchInput input)
    {
        var busyChatIds = new List<ChatId>();
        lock (_lock) {
            // Checked and not ringing: dropped, or dead candidates would pile up for the scope's lifetime.
            foreach (var chatId in input.CheckedChatIds)
                if (!input.Rings.Any(r => r.ChatId == chatId)) {
                    RemoveCandidate(chatId);
                    _busyAckedChatIds.Remove(chatId);
                }

            foreach (var ring in input.Rings) {
                var chatId = ring.ChatId;
                // Against the live slot, not the input's: a claim earlier in this pass has to count.
                var outcome = DecideSearch(
                    _callChatId.Value, _activeCall.Value, chatId, _busyAckedChatIds.Contains(chatId));
                switch (outcome) {
                case SearchOutcome.Claim:
                    _callChatId.Value = chatId;
                    break;
                case SearchOutcome.Busy:
                    _busyAckedChatIds.Add(chatId);
                    busyChatIds.Add(chatId);
                    break;
                }
            }
        }
        foreach (var chatId in busyChatIds) {
            CallDebugLog?.LogInformation("CALL_TRACE: Busy #{ChatId}", chatId);
            _ = ConfirmRing(chatId, RingAck.Busy);
            Bridge?.DismissCallNotification(chatId);
        }
    }

    private async Task SyncActiveCallNotifications(CancellationToken cancellationToken)
    {
        // Off Android the primary ring trigger; on Android the safety net for a push dropped while the
        // scope is alive. The search confirms each ring against the session.
        var cNotifications = await Computed
            .Capture(() => Notifications.ListActive(Session, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var c in cNotifications.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;

            foreach (var notification in c.Value)
                if (notification is CallNotification call)
                    AddCandidate(call.ChatId);
        }
    }

    private async Task ConfirmRing(ChatId chatId, RingAck ack)
    {
        // Telemetry only (see RingAck), so it's fire-and-forget: a slow or failed ack never holds up the ring.
        try {
            await LiveSessionUI.ConfirmRing(chatId, ack, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "ConfirmRing({Ack}) #{ChatId} failed", ack, chatId);
        }
    }

    // Nested types

    protected sealed record HoldingInput(ChatId? SlotChatId, ActiveCall? Call, CallFacts Facts);

    protected sealed record SearchInput(ImmutableList<ChatId> CheckedChatIds, ImmutableList<IncomingCall> Rings);
}

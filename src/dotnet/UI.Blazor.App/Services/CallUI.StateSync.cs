using ActualChat.Live;

namespace ActualChat.UI.Blazor.App.Services;

public partial class CallUI
{
    // How long a call outlives losing the server. The server ends a peer call ~12 s after a client's
    // connection drops (ParticipationDisconnectGrace + CallLeaveGrace), so past this nobody hears the call
    // and its end can't reach this client. A server restart shorter than this doesn't end a call.
    private static readonly TimeSpan OfflineCallTimeout = TimeSpan.FromSeconds(20);

    private Computed<UserCall?>? _cMyCall;

    // Public methods

    // A push, a notification list or a native ring is a hint that the answer changed, never the answer
    // itself: all any of them does is make the projection re-read it now instead of on the next change.
    public void Touch()
        // Paired with the publication in SyncMyCall.
        => Volatile.Read(ref _cMyCall)?.Invalidate();

    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(SyncMyCall),
            AsyncChain.From(SyncOfflineCall),
            AsyncChain.From(SyncCallActivity),
            AsyncChain.From(SyncOutputRoute),
            AsyncChain.From(SyncOutputRouteTakeover),
            AsyncChain.From(SyncScreenOffAtEar),
        };
        var retryDelays = RetryDelaySeq.Exp(0.5, 10);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken);
    }

    internal static ActiveCall? Reconcile(ActiveCall? held, UserCall? myCall, CallGestures gestures)
    {
        // An answer about a call this client left is no news: the server names it until the leave reaches it.
        if (myCall is not null && gestures.IsLeft(myCall))
            return held;

        // A gesture whose request is still on its way holds its call: the server can't name it yet.
        var isPending = held is not null && (held.CallId is null || held.CallId == gestures.AcceptingCallId);
        if (myCall is null)
            // GetMyCall reads the server and nothing else, so this is its word that I have no call.
            return isPending ? held : null;
        if (myCall.Phase == CallPhase.Ended)
            return held is not null && held.CallId == myCall.CallId && !isPending ? null : held;

        var server = new ActiveCall(
            myCall.ChatId, myCall.Role, myCall.Phase, myCall.PeerId, myCall.HasVideo, myCall.CallId);
        // Another call: the server arbitrated, and whatever the slot held is over or never was.
        if (held is null || !held.IsCall(server.ChatId, server.CallId))
            return server;

        // A just-accepted ring is Active here before the server says so - keep the phase that went further.
        return held.Phase == CallPhase.Active && server.Phase != CallPhase.Active
            ? held with { CallId = server.CallId }
            : server;
    }

    // Placing a call is itself the intent to talk, so an answered one puts the caller on the line - once.
    // Read from the slot it replaced, not latched: a latch outlives a slot this client frees itself, and
    // the next call to that chat then connects with no audio.
    internal static bool ShouldStartCallAudio(ActiveCall? held, [NotNullWhen(true)] ActiveCall? next)
        => next is { Role: CallRole.Caller, Phase: CallPhase.Active }
            && (held is not { Role: CallRole.Caller, Phase: CallPhase.Active } || !held.IsSameCall(next));

    // Private methods

    private async Task SyncMyCall(CancellationToken cancellationToken)
    {
        // GetMyCall is NoCache: every value it has is the server's answer - it waits out a disconnect
        // instead of standing in for one, so the slot keeps its call meanwhile.
        var c = await Computed
            .Capture(() => LiveSessions.GetMyCall(Session, ClientId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (true) {
            // Published for Touch, which runs on whatever thread a push or a native ring arrives on.
            Volatile.Write(ref _cMyCall, c);
            if (!c.HasError)
                Apply(c.Value);
            await c.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            c = await c.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private void Apply(UserCall? myCall)
    {
        ChatId? ringingChatId = null;
        ChatId? joinedChatId = null;
        ChatId? unansweredChatId = null;
        lock (_lock) {
            var held = _activeCall.Value;
            var next = Reconcile(held, myCall, new CallGestures(_leftCallIds, _cancelledChatId, _acceptingCallId));
            CallDebugLog?.LogInformation(
                "CALL_TRACE: MyCall #{CallId} {Role}/{Phase} → slot #{Next}",
                myCall?.CallId?.Value ?? myCall?.ChatId.Value, myCall?.Role, myCall?.Phase,
                next?.CallId?.Value ?? next?.ChatId.Value);
            // A left call the server no longer names live is one it has caught up on.
            if (myCall is null || myCall.Phase == CallPhase.Ended || !_leftCallIds.Contains(myCall.CallId))
                _leftCallIds.Clear();
            if (next == held)
                return;

            SetActiveCallUnsafe(next);
            if (next is { Role: CallRole.Callee, Phase: CallPhase.Ringing })
                ringingChatId = next.ChatId;
            if (ShouldStartCallAudio(held, next))
                joinedChatId = next.ChatId;
            // A dialing call that leaves the slot was never picked up - a decline reads the same to the
            // caller. The user's own cancel never gets here: CancelCall frees the slot first.
            if (held is { Role: CallRole.Caller, Phase: CallPhase.Dialing } && !held.IsSameCall(next))
                unansweredChatId = held.ChatId;
        }
        if (ringingChatId is { } chatId)
            _ = SendRingAck(chatId, RingAck.Ringing);
        if (unansweredChatId is { } unanswered)
            SystemCallUI.OnOutgoingCallStatusChanged(unanswered, CallerStatus.NoAnswer);
        if (joinedChatId is { } joined) {
            // Reported before the join: it is what flips the platform call UI to connected, and the
            // join can sit on a permission prompt for as long as it likes.
            SystemCallUI.OnOutgoingCallStatusChanged(joined, CallerStatus.Active);
            _ = StartAnsweredCallAudio(joined);
        }
    }

    private async Task SyncOfflineCall(CancellationToken cancellationToken)
    {
        var cIsOffline = await Computed
            .Capture(() => IsCallOffline(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (true) {
            cIsOffline = await cIsOffline.When(x => x, cancellationToken).ConfigureAwait(false);
            using var cts = cancellationToken.CreateLinkedTokenSource();
            var whenBack = cIsOffline.When(x => !x, cts.Token);
            await Task.WhenAny(whenBack, Clocks.CpuClock.Delay(OfflineCallTimeout, cts.Token)).ConfigureAwait(false);
            cts.CancelAndDisposeSilently();
            if (!whenBack.IsCompletedSuccessfully && _activeCall.Value is { } call)
                await Hub.Dispatcher.InvokeAsync(() => EndOfflineCall(call)).ConfigureAwait(false);
            cIsOffline = await cIsOffline.When(x => !x, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EndOfflineCall(ActiveCall call)
    {
        // Ended as the user would end it: the cancel and the stopped presence reach the server once it is back.
        var chatId = call.ChatId;
        Log.LogWarning("Call #{CallId}: no connection for {Timeout}, ending it",
            call.CallId?.Value ?? chatId.Value, OfflineCallTimeout.ToShortString());
        try {
            if (call is { Role: CallRole.Caller, Phase: CallPhase.Dialing })
                await CancelCall(chatId, CancellationToken.None).ConfigureAwait(true);
            else if (call.Phase == CallPhase.Ringing)
                DropRing(chatId, call.CallId);
            else
                await HangUp(chatId).ConfigureAwait(true);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Ending the offline call failed for chat #{ChatId}", chatId);
        }
    }

    private async Task StartAnsweredCallAudio(ChatId chatId)
    {
        try {
            await StartCallAudio(chatId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Couldn't join the answered call in chat #{ChatId}", chatId);
            Release(chatId);
        }
    }

    private async Task SendRingAck(ChatId chatId, RingAck ack)
    {
        // Telemetry only (see RingAck), so it's fire-and-forget: a slow or failed ack never holds up the ring.
        try {
            await ConfirmRing(chatId, ack, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "ConfirmRing({Ack}) #{ChatId} failed", ack, chatId);
        }
    }

    // Nested types

    // What this client's own gestures add to the server's answer: the calls it left, which the server may
    // still name, and the answer it sent that the server may not have taken yet.
    internal readonly record struct CallGestures(
        IReadOnlyCollection<CallId>? LeftCallIds,
        ChatId? CancelledChatId = null,
        CallId? AcceptingCallId = null)
    {
        // A call cancelled before it was named is known by chat alone, and only as the call I placed.
        public bool IsLeft(UserCall myCall)
            => LeftCallIds?.Contains(myCall.CallId) == true
                || (CancelledChatId == myCall.ChatId && myCall.Role == CallRole.Caller);
    }
}

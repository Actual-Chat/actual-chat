using ActualChat.Live;

namespace ActualChat.UI.Blazor.App.Services;

public partial class CallUI
{
    // Safe rather than Precise: a disconnected client counts as synchronized, so an offline read still
    // frees the slot - the intent grace in Reconcile is what covers a short disconnect.
    private static readonly ComputedSynchronizer Synchronizer = ComputedSynchronizer.Safe.Instance;

    private volatile Computed<UserCall?>? _cMyCall;

    // Public methods

    // A push, a notification list or a native ring is a hint that the answer changed, never the answer
    // itself: all any of them does is make the projection re-read it now instead of on the next change.
    public void Touch()
        => _cMyCall?.Invalidate();

    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(SyncMyCall)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(0.5, 10), Log)
            .Run(cancellationToken);

    internal static ActiveCall? Reconcile(UserCall? myCall, CallIntentView intent)
    {
        if (myCall is null)
            // "No call" is also what a disconnected client reads, so a fresh gesture outlives it.
            return intent is { IsFresh: true, Call: { } intended } ? intended : null;

        var server = new ActiveCall(myCall.ChatId, myCall.Role, myCall.Phase, myCall.PeerId, myCall.HasVideo);
        if (!intent.IsFresh || intent.ChatId != myCall.ChatId)
            return server;
        if (intent.Call is null)
            return null; // Just left this call, and the server hasn't caught up yet

        // A just-accepted ring is Active here before the server says so - keep the phase that went further.
        return intent.Call.Phase == CallPhase.Active && server.Phase != CallPhase.Active
            ? intent.Call
            : server;
    }

    // Placing a call is itself the intent to talk, so an answered one puts the caller on the line - once.
    // Read from the slot it replaced, not latched: a latch outlives a slot this client frees itself, and
    // the next call to that chat then connects with no audio.
    internal static bool ShouldStartCallAudio(ActiveCall? held, [NotNullWhen(true)] ActiveCall? next)
        => next is { Role: CallRole.Caller, Phase: CallPhase.Active }
            && (held is not { Role: CallRole.Caller, Phase: CallPhase.Active } || held.ChatId != next.ChatId);

    // Private methods

    private async Task SyncMyCall(CancellationToken cancellationToken)
    {
        var c = await Computed
            .Capture(() => LiveSessions.GetMyCall(Session, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (true) {
            _cMyCall = c;
            // GetMyCall is ReturnDefault, so every re-read answers "no call" before the server's real
            // answer lands - and Touch() re-reads on every ring push. Applying that would drop the slot
            // mid-ring, taking the call screen and the ringtone with it, until the answer put it back.
            if (!c.HasError && (c.Value is not null || c.IsSynchronized(Synchronizer)))
                Apply(c.Value);

            // An intent outliving the answer has to expire on its own: the answer that ignores it
            // may never change again, so nothing else would ever re-run the rule.
            if (IntentExpiryDelay() is { } delay) {
                using var cts = cancellationToken.CreateLinkedTokenSource();
                await Task.WhenAny(
                        c.WhenInvalidated(cts.Token),
                        Clocks.CpuClock.Delay(delay, cts.Token))
                    .ConfigureAwait(false);
                cts.CancelAndDisposeSilently();
            }
            else
                await c.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            c = await c.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private TimeSpan? IntentExpiryDelay()
    {
        lock (_lock) {
            if (_intent is not { } intent)
                return null;

            var delay = intent.At + IntentGrace - Now;
            return delay > TimeSpan.Zero ? delay + TimeSpan.FromMilliseconds(50) : TimeSpan.Zero;
        }
    }

    private void Apply(UserCall? myCall)
    {
        ChatId? ringingChatId = null;
        ChatId? joinedChatId = null;
        ChatId? unansweredChatId = null;
        lock (_lock) {
            var held = _activeCall.Value;
            var intent = CallIntentView.Of(_intent, Now, IntentGrace);
            var next = Reconcile(myCall, intent);
            CallDebugLog?.LogInformation(
                "CALL_TRACE: MyCall #{ChatId} {Role}/{Phase} → slot #{Next}",
                myCall?.ChatId, myCall?.Role, myCall?.Phase, next?.ChatId);
            // The intent stops competing once it's been answered - confirmed or overruled - or aged out.
            if (!intent.IsFresh || myCall?.ChatId == intent.ChatId)
                _intent = null;
            // Set before the early return below: the slot can stay put while the server's answer for it
            // arrives, and that arrival is exactly what the outgoing screens wait for.
            _serverCallChatId.Value = myCall?.ChatId;
            if (next == held)
                return;

            _activeCall.Value = next;
            if (next is { Role: CallRole.Callee, Phase: CallPhase.Ringing })
                ringingChatId = next.ChatId;
            if (ShouldStartCallAudio(held, next))
                joinedChatId = next.ChatId;
            // A dialing call that leaves the slot was never picked up - a decline reads the same to the
            // caller. The user's own cancel never gets here: CancelCall frees the slot first.
            if (held is { Role: CallRole.Caller, Phase: CallPhase.Dialing } && next?.ChatId != held.ChatId)
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

    // The intent as the pure reconciliation rule sees it: what it wanted, for which chat, and whether it
    // is still young enough to outrank the server's answer.
    internal readonly record struct CallIntentView(ActiveCall? Call, ChatId ChatId, bool IsFresh)
    {
        public static CallIntentView Of(CallIntent? intent, Moment now, TimeSpan grace)
            => intent is null
                ? default
                : new CallIntentView(intent.Call, intent.ChatId, now - intent.At < grace);
    }
}

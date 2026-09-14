using ActualChat.Live;
using ActualChat.Notifications;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// The one call this client is in - incoming or outgoing, ringing, dialing or connected. While the slot
/// is held every other ring is answered Busy and no new call can start.
/// </summary>
public partial class CallUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    // Give up on an outgoing call whose session never shows up as dialing.
    private static readonly TimeSpan DialingWaitTimeout = TimeSpan.FromSeconds(15);

    private readonly Lock _lock = new();
    private readonly MutableState<ImmutableList<ChatId>> _ringingChatIds;
    private readonly MutableState<ChatId?> _callChatId;
    private readonly MutableState<ActiveCall?> _activeCall;
    // Rings answered Busy while the slot is held - ListActive repeats a ring on every change.
    private readonly HashSet<ChatId> _busyAckedChatIds = [];

    private IIncomingCallsBridge? Bridge { get; }
    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IAuthors Authors => Hub.Authors;
    private INotifications Notifications => Hub.Notifications;
    private Moment Now => Clocks.CpuClock.Now;
    private ILogger? CallDebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.AndroidIncomingCalls);

    public CallUI(AppUIHub hub) : base(hub)
    {
        Bridge = hub.Services.GetService<IIncomingCallsBridge>();
        _ringingChatIds = StateFactory.NewMutable(
            ImmutableList<ChatId>.Empty,
            StateCategories.Get(GetType(), "RingingChatIds"));
        _callChatId = StateFactory.NewMutable(
            (ChatId?)null,
            StateCategories.Get(GetType(), "CallChatId"));
        _activeCall = StateFactory.NewMutable(
            (ActiveCall?)null,
            StateCategories.Get(GetType(), "ActiveCall"));
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual Task<ActiveCall?> GetActiveCall(CancellationToken cancellationToken)
        => _activeCall.Use(cancellationToken);

    [ComputeMethod]
    public virtual Task<ChatId?> GetCallChatId(CancellationToken cancellationToken)
        => _callChatId.Use(cancellationToken);

    public ChatId? GetCallChatIdNonComputed()
        => _callChatId.Value;

    [ComputeMethod]
    public virtual async Task<bool> CanStartCall(CancellationToken cancellationToken)
        => await GetCallChatId(cancellationToken).ConfigureAwait(false) is null;

    [ComputeMethod]
    public virtual async Task<ChatId?> GetDialingOutChatId(CancellationToken cancellationToken)
    {
        // The slot is claimed before the StartCall RPC, but the outgoing screens read the invitee from the
        // session, and a refused call must not ring back first - so they wait for the server's dialing.
        var call = await GetActiveCall(cancellationToken).ConfigureAwait(false);
        if (call is not { Origin: CallOrigin.Outgoing, Phase: CallPhase.Dialing })
            return null;

        var live = await LiveSessionUI.Get(call.ChatId, cancellationToken).ConfigureAwait(false);
        return live is { Kind: LiveSessionKind.Call, Conversation: null } ? call.ChatId : null;
    }

    [ComputeMethod]
    public virtual async Task<IncomingCall?> GetRingingCall(ChatId chatId, CancellationToken cancellationToken)
    {
        // Straight from the session, for any chat - whether that ring may hold the slot is the search's call.
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
        // No conversation yet means nobody has answered; once someone does, it's no longer an incoming ring.
        if (live is not { Kind: LiveSessionKind.Call, Conversation: null })
            return null;
        if (live.Host == ownAuthorId)
            return null;

        var invite = live.Invites.FirstOrDefault(i => i.InviteeId == ownAuthorId);
        if (invite is not { Status: CallInviteStatus.Ringing })
            return null;

        return new IncomingCall(live.ChatId, live.Host, live.Rules.VideoAllowed);
    }

    public void AddCandidate(ChatId chatId)
    {
        lock (_lock) {
            var chatIds = _ringingChatIds.Value;
            if (!chatIds.Contains(chatId))
                _ringingChatIds.Value = chatIds.Add(chatId);
        }
    }

    public bool TryClaimOutgoing(ChatId chatId, bool hasVideo)
    {
        lock (_lock) {
            if (_callChatId.Value is not null)
                return false;

            _callChatId.Value = chatId;
            _activeCall.Value = new ActiveCall(chatId, CallOrigin.Outgoing, CallPhase.Dialing, null, hasVideo);
            return true;
        }
    }

    public bool TryCommitAccept(IncomingCall call)
    {
        // From a free slot this claims it too: Answer on a notification can land before the search does.
        var chatId = call.ChatId;
        lock (_lock) {
            var slotChatId = _callChatId.Value;
            if (slotChatId is not null && slotChatId != chatId)
                return false;

            _callChatId.Value = chatId;
            _activeCall.Value = new ActiveCall(
                chatId, CallOrigin.Incoming, CallPhase.Active, call.Caller, call.HasVideo);
            RemoveCandidate(chatId);
            _busyAckedChatIds.Remove(chatId);
            return true;
        }
    }

    public void DropRing(ChatId chatId)
    {
        // The slot goes with the ring only while that ring is what holds it: the dismissal push our own
        // accept triggers must not end the call it just started.
        lock (_lock) {
            RemoveCandidate(chatId);
            _busyAckedChatIds.Remove(chatId);
            if (_callChatId.Value == chatId && _activeCall.Value is null or { Phase: CallPhase.Ringing })
                ReleaseUnsafe();
        }
    }

    public void Release(ChatId chatId)
    {
        lock (_lock) {
            if (_callChatId.Value == chatId)
                ReleaseUnsafe();
        }
    }

    // Protected/internal methods

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

    // Caller must hold _lock.
    private void ReleaseUnsafe()
    {
        if (_callChatId.Value is { } chatId)
            RemoveCandidate(chatId);
        _activeCall.Value = null;
        _callChatId.Value = null;
        _busyAckedChatIds.Clear();
    }

    // Caller must hold _lock.
    private void RemoveCandidate(ChatId chatId)
    {
        var chatIds = _ringingChatIds.Value;
        if (chatIds.Contains(chatId))
            _ringingChatIds.Value = chatIds.Remove(chatId);
    }

    // Nested types

    protected sealed record HoldingInput(ChatId? SlotChatId, ActiveCall? Call, CallFacts Facts);

    protected sealed record SearchInput(ImmutableList<ChatId> CheckedChatIds, ImmutableList<IncomingCall> Rings);
}

using ActualChat.Localization;
using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
using ActualLab.Generators;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// The one call this client is in. A call enters the slot on a gesture made here or a ring the server
/// names, and leaves it on a hang-up made here or the server's word that it is over - never on the
/// server merely not naming it yet. <see cref="Reconcile"/> is that rule.
/// </summary>
public partial class CallUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    private readonly Lock _lock = new();
    private readonly MutableState<ActiveCall?> _activeCall;
    // The calls this client left that the server may still name: it does until the leave reaches it, and
    // for a group call it goes on without this client, for as long as it lasts.
    private readonly List<CallId> _leftCallIds = [];
    // A call cancelled before its StartCall named it: until that id arrives, it is known by chat alone.
    private ChatId? _cancelledChatId;
    // The ring answered here whose AcceptCall is still on its way: the server ends its ring claim on a late
    // answer, and only takes it back once the answer lands.
    private CallId? _acceptingCallId;
    // The StartCall still on its way, and the last CancelCall sent. The two go to the server in the
    // order they were made: a cancel needs the id its StartCall answers with, and a redial must not
    // get ahead of the cancel before it.
    private PlacedCall? _placedCall;
    private Task _whenCancelled = Task.CompletedTask;

    private IIncomingCallsBridge? Bridge { get; }
    private ISystemCallUI SystemCallUI => field ??= Hub.Services.GetRequiredService<ISystemCallUI>();
    private ILiveSessions LiveSessions => Hub.LiveSessions;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;
    private AudioRecorder AudioRecorder => Hub.AudioRecorder;
    private IAuthors Authors => Hub.Authors;
    private ILogger? CallDebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.AndroidIncomingCalls);

    // Names this running client to the server, which shows a placed or answered call only to the client
    // that placed or answered it. It lives exactly as long as the call's audio can: a reload is a new client.
    public string ClientId { get; } = RandomStringGenerator.Default.Next();

    public CallUI(AppUIHub hub) : base(hub)
    {
        Bridge = hub.Services.GetService<IIncomingCallsBridge>();
        _activeCall = StateFactory.NewMutable(
            (ActiveCall?)null,
            StateCategories.Get(GetType(), "ActiveCall"));
        _pickedOutputRouteId = StateFactory.NewMutable(
            (string?)null,
            StateCategories.Get(GetType(), "PickedOutputRouteId"));
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual Task<ActiveCall?> GetActiveCall(CancellationToken cancellationToken)
        => _activeCall.Use(cancellationToken);

    public ActiveCall? GetActiveCallNonComputed()
        => _activeCall.Value;

    [ComputeMethod]
    public virtual async Task<ChatId?> GetCallChatId(CancellationToken cancellationToken)
        => (await GetActiveCall(cancellationToken).ConfigureAwait(false))?.ChatId;

    public ChatId? GetCallChatIdNonComputed()
        => _activeCall.Value?.ChatId;

    // Null also while the slot holds the chat's call but the server hasn't named it yet.
    public CallId? GetCallIdNonComputed(ChatId chatId)
        => _activeCall.Value is { } call && call.ChatId == chatId ? call.CallId : null;

    [ComputeMethod]
    public virtual async Task<bool> CanStartCall(CancellationToken cancellationToken)
        => await GetCallChatId(cancellationToken).ConfigureAwait(false) is null;

    [ComputeMethod]
    public virtual async Task<ChatId?> GetDialingOutChatId(CancellationToken cancellationToken)
    {
        // The ringback follows this rather than the slot: the slot is claimed before the StartCall RPC,
        // and a refused call must not ring back first. The screens don't wait - they show on the click.
        // A named call is one the server took: the id comes from its StartCall answer or its own claim.
        var call = await GetActiveCall(cancellationToken).ConfigureAwait(false);
        return call is { Role: CallRole.Caller, Phase: CallPhase.Dialing, CallId: not null } ? call.ChatId : null;
    }

    public static AuthorId? GetPeerAuthorId(ChatId chatId, UserId ownUserId)
        // A peer chat's author ids follow from its user ids, so the one invitee the header's call
        // button leaves to the server is already known on the client - no read, nothing to wait for.
        => chatId is PeerChatId peerChatId && peerChatId.HasUser(ownUserId)
            ? peerChatId.AnotherAuthorId(ownUserId)
            : null;

    public async Task StartCall(
        ChatId chatId,
        ApiArray<AuthorId> invitees,
        bool hasVideo,
        CancellationToken cancellationToken)
    {
        // Ask on the click itself: it's a real user gesture, the request can't yet race the ringback,
        // and the answered call's join re-reads the (now cached) verdict without prompting again.
        // A call the caller can't be heard on isn't worth ringing the other side for, so a denial
        // stops it here instead of falling back to a listen-only call as the callee side does.
        if (!await AudioRecorder.MicrophonePermission.CheckOrRequest(cancellationToken).ConfigureAwait(false)) {
            Hub.ToastUI.Show(L.Call_NoMicrophoneAccess, "icon-phone-hang-up", ToastDismissDelay.Short);
            return;
        }
        // Taken before the RPC, so the screens follow the gesture rather than the round trip. A single
        // invitee, or a peer chat's other side, is the peer the screens name; a call to several has none.
        var peerId = invitees.Count == 1
            ? invitees[0]
            : GetPeerAuthorId(chatId, Hub.AccountUI.OwnAccount.Value.Id);
        if (!TryClaimOutgoing(chatId, peerId, hasVideo)) {
            Hub.ToastUI.Show(L.Call_AlreadyInCall, "icon-phone-hang-up", ToastDismissDelay.Short);
            return;
        }

        var whenPlaced = PlaceCall(chatId, invitees, hasVideo, cancellationToken);
        CallId callId;
        try {
            callId = await whenPlaced.ConfigureAwait(false);
        }
        catch (Exception e) {
            // Cancelled meanwhile: the slot is free, or already taken by the next call.
            var isCancelled = !EndPlacing(whenPlaced);
            if (!isCancelled)
                Release(chatId);
            if (e is OperationCanceledException)
                throw;
            if (isCancelled)
                return;

            // Only StandardError.Constraint carries user-facing text - the peer-call gate, or this
            // user being in a call already, possibly on another device.
            Log.LogWarning(e, "StartCall failed for chat #{ChatId}", chatId);
            var message = e is InvalidOperationException ? e.Message : L.Call_CouldntStart;
            Hub.ToastUI.Show(message, "icon-phone-hang-up", ToastDismissDelay.Short);
            return;
        }

        if (!EndPlacing(whenPlaced))
            return; // Cancelled meanwhile: the cancel took the id and the slot is no longer this call's
        if (!NameOutgoing(chatId, callId))
            return;

        SystemCallUI.OnOutgoingCallStarted(chatId, callId, hasVideo);
    }

    public Task CancelCall(ChatId chatId, CancellationToken cancellationToken)
    {
        Task whenCancelled;
        lock (_lock) {
            // Read before the release: the server must cancel the call this client held, not whichever
            // one the chat is in by the time the request lands.
            var callId = GetCallIdNonComputed(chatId);
            var placedCall = _placedCall is { } call && call.ChatId == chatId ? call : null;
            if (placedCall is not null)
                _placedCall = null;
            if (_activeCall.Value?.ChatId == chatId)
                ReleaseUnsafe(chatId);
            if (callId is null && placedCall is not null)
                _cancelledChatId = chatId;
            whenCancelled = _whenCancelled = Cancel(callId, placedCall?.WhenPlaced);
        }
        SystemCallUI.OnOutgoingCallCancelled(chatId);
        return whenCancelled;

        async Task Cancel(CallId? callId, Task<CallId>? whenPlaced) {
            // The slot is free already; the server hears of it once it has said which call it placed.
            if (callId is null && whenPlaced is not null) {
                callId = (await whenPlaced.ResultAwait(false)).ValueOrDefault;
                lock (_lock) {
                    if (callId is not null)
                        _leftCallIds.Add(callId);
                    if (_cancelledChatId == chatId)
                        _cancelledChatId = null;
                }
            }
            if (callId is null)
                return; // StartCall failed

            await LiveSessions.CancelCall(Session, callId, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task AcceptCall(CallId callId, CancellationToken cancellationToken)
        => LiveSessions.AcceptCall(Session, callId, ClientId, cancellationToken);

    public Task DeclineCall(CallId callId, CancellationToken cancellationToken)
        => LiveSessions.DeclineCall(Session, callId, cancellationToken);

    public Task ConfirmRing(ChatId chatId, RingAck ack, CancellationToken cancellationToken)
        => LiveSessions.ConfirmRing(Session, chatId, ack, cancellationToken);

    public async Task StartCallAudio(ChatId chatId, CancellationToken cancellationToken)
    {
        // Listening goes first, so a pending OS mic prompt can't fail the server's connect grace check;
        // a denied mic still joins the call, listening only.
        await ChatAudioUI.SetListeningState(chatId, true).ConfigureAwait(true);
        var hasMic = await AudioRecorder.MicrophonePermission
            .CheckOrRequest(cancellationToken)
            .ConfigureAwait(true);
        if (hasMic)
            await ChatAudioUI.SetRecordingChatId(chatId).ConfigureAwait(true);
    }

    public Task HangUp(ChatId chatId)
    {
        // Leaving the call server-side follows from the stopped audio, through the same SetParticipation
        // path as any other presence change - see LiveSessionUI.SyncParticipations. The server's claim
        // goes with that presence, on its next read of this call.
        Release(chatId);
        return StopCallMedia(chatId);
    }

    public Task EndCallMedia(ChatId chatId)
        // A peer call ends when either party leaves, so it takes the other's media along. Any other call ends
        // only once its last party has hung up, so whatever media this client still runs there is its own.
        => chatId.Kind == ChatKind.Peer ? StopCallMedia(chatId) : Task.CompletedTask;

    public async Task StopCallMedia(ChatId chatId)
    {
        // The camera and the screencast belong to the chat, not the call, so nothing else stops them.
        ChatVideoUI.LeaveVideoSession(chatId);
        await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(true);
        await ChatAudioUI.SetListeningState(chatId, false).ConfigureAwait(true);
    }

    public bool TryClaimOutgoing(ChatId chatId, AuthorId? peerId, bool hasVideo)
    {
        lock (_lock) {
            if (_activeCall.Value is not null)
                return false;

            SetActiveCallUnsafe(new ActiveCall(chatId, CallRole.Caller, CallPhase.Dialing, peerId, hasVideo));
        }

        return true;
    }

    public bool TryCommitAccept(ChatId chatId, CallId? callId = null)
    {
        // From a free slot this claims it too: Answer on a notification can land before the projection does.
        lock (_lock) {
            var heldCall = _activeCall.Value;
            if (heldCall is not null && heldCall.ChatId != chatId)
                return false;

            // The slot holds another call to this chat than the one answered: nothing of it rides along.
            if (heldCall is not null && !heldCall.IsCall(chatId, callId))
                heldCall = null;
            callId ??= heldCall?.CallId;
            // Answered again, a call this client left is wanted after all.
            if (callId is not null)
                _leftCallIds.Remove(callId);
            _acceptingCallId = callId;
            // Caller and video ride along from the ring when the slot already holds it; answering
            // before the projection lands leaves them unknown until the server's own answer does.
            SetActiveCallUnsafe(new ActiveCall(chatId, CallRole.Callee, CallPhase.Active,
                heldCall?.PeerId, heldCall?.HasVideo ?? false, callId));
        }

        return true;
    }

    // The answer's AcceptCall has returned, taken or refused: from here the server's word on the call stands.
    public void EndAccept(CallId? callId)
    {
        lock (_lock) {
            if (_acceptingCallId == callId)
                _acceptingCallId = null;
        }
    }

    public bool DropRing(ChatId chatId, CallId? callId = null)
    {
        // Reports whether the slot held the call, in any phase. The slot goes only while the ring itself holds
        // it: the dismissal push our own accept triggers must not end the call it just started.
        lock (_lock) {
            if (_activeCall.Value is not { } call || !call.IsCall(chatId, callId))
                return false;

            if (call.Phase == CallPhase.Ringing)
                ReleaseUnsafe(chatId);
        }

        return true;
    }

    public void Release(ChatId chatId)
    {
        lock (_lock) {
            if (_activeCall.Value?.ChatId == chatId)
                ReleaseUnsafe(chatId);
        }
    }

    // Sends StartCall in its turn - after the cancel made before it, if that one is still on its way.
    internal Task<CallId> PlaceCall(
        ChatId chatId,
        ApiArray<AuthorId> invitees,
        bool hasVideo,
        CancellationToken cancellationToken)
    {
        lock (_lock) {
            var whenPlaced = Place(_whenCancelled);
            _placedCall = new PlacedCall(chatId, whenPlaced);
            return whenPlaced;
        }

        async Task<CallId> Place(Task whenCancelled) {
            await whenCancelled.SilentAwait(false);
            return await LiveSessions.StartCall(Session, chatId, invitees, hasVideo, ClientId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // Private methods

    // Reports whether the placing is still this client's to finish - false once CancelCall took it over.
    private bool EndPlacing(Task<CallId> whenPlaced)
    {
        lock (_lock) {
            if (!ReferenceEquals(_placedCall?.WhenPlaced, whenPlaced))
                return false;

            _placedCall = null;
            return true;
        }
    }

    // Reports whether the slot holds the call.
    private bool NameOutgoing(ChatId chatId, CallId callId)
    {
        // The slot was claimed on the click, before the server had a call to name. Only that claim
        // takes the id: if GetMyCall got here first, the slot already has it.
        lock (_lock) {
            if (_activeCall.Value is not { Role: CallRole.Caller } call || call.ChatId != chatId)
                return false;
            if (call.CallId is not null)
                return call.CallId == callId;

            // Placed right after a hang-up, the call can be the very one just left - the server joins a call
            // that is still connected. It is wanted again, so it is no longer "left".
            _leftCallIds.Remove(callId);
            SetActiveCallUnsafe(call with { CallId = callId });
        }
        // The answers naming it may have been kept off the slot as the left call's; nothing else re-reads them.
        Touch();
        return true;
    }

    // Caller must hold _lock.
    private void ReleaseUnsafe(ChatId chatId)
    {
        // The server goes on naming this call mine until my absence reaches it, and that answer must not put
        // the screens back up.
        if (_activeCall.Value is { CallId: { } callId } && callId.ChatId == chatId && !_leftCallIds.Contains(callId))
            _leftCallIds.Add(callId);
        SetActiveCallUnsafe(null);
    }

    // Caller must hold _lock.
    private void SetActiveCallUnsafe(ActiveCall? call)
    {
        // Each change is logged: the lines between a call taking the slot and leaving it are that call's
        var held = _activeCall.Value;
        if (call != held)
            Log.LogInformation("Call slot: {Held} -> {Call}", held?.ToString() ?? "none", call?.ToString() ?? "none");
        _activeCall.Value = call;
    }

    // Nested types

    private sealed record PlacedCall(ChatId ChatId, Task<CallId> WhenPlaced);
}

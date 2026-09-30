using ActualChat.Localization;
using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
using ActualLab.Generators;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// The one call this client shows: a projection of the server's <see cref="ILiveSessions.GetMyCall"/>,
/// plus the intent of a gesture this client just made and the server hasn't confirmed yet.
/// </summary>
public partial class CallUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    // How long a gesture's own view of the slot survives an answer that doesn't show it yet: the RPC
    // round trip, and the reconnect after a short disconnect - where the answer is "no call" (#4532).
    private static readonly TimeSpan IntentGrace = TimeSpan.FromSeconds(10);

    private readonly Lock _lock = new();
    private readonly MutableState<ActiveCall?> _activeCall;
    // The call the server last named as mine. The slot blends this with a gesture it hasn't answered
    // yet, so it can't tell the two apart - and a screen that must wait for the server needs to.
    private readonly MutableState<ChatId?> _serverCallChatId;
    private CallIntent? _intent;

    private IIncomingCallsBridge? Bridge { get; }
    private ISystemCallUI SystemCallUI => field ??= Hub.Services.GetRequiredService<ISystemCallUI>();
    private ILiveSessions LiveSessions => Hub.LiveSessions;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private AudioRecorder AudioRecorder => Hub.AudioRecorder;
    private IAuthors Authors => Hub.Authors;
    private Moment Now => Clocks.CpuClock.Now;
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
        _serverCallChatId = StateFactory.NewMutable(
            (ChatId?)null,
            StateCategories.Get(GetType(), "ServerCallChatId"));
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
        var call = await GetActiveCall(cancellationToken).ConfigureAwait(false);
        if (call is not { Role: CallRole.Caller, Phase: CallPhase.Dialing })
            return null;

        var serverChatId = await _serverCallChatId.Use(cancellationToken).ConfigureAwait(false);
        return serverChatId == call.ChatId ? call.ChatId : null;
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
        // invitee is the peer the screens name; for a group the server's invites decide, so it waits.
        var peerId = invitees.Count == 1
            ? invitees[0]
            : GetPeerAuthorId(chatId, Hub.AccountUI.OwnAccount.Value.Id);
        if (!TryClaimOutgoing(chatId, peerId, hasVideo)) {
            Hub.ToastUI.Show(L.Call_AlreadyInCall, "icon-phone-hang-up", ToastDismissDelay.Short);
            return;
        }

        CallId? callId;
        try {
            callId = await LiveSessions.StartCall(Session, chatId, invitees, hasVideo, ClientId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            Release(chatId);
            if (e is OperationCanceledException)
                throw;

            // Only StandardError.Constraint carries user-facing text - the peer-call gate, or this
            // user being in a call already, possibly on another device.
            Log.LogWarning(e, "StartCall failed for chat #{ChatId}", chatId);
            var message = e is InvalidOperationException ? e.Message : L.Call_CouldntStart;
            Hub.ToastUI.Show(message, "icon-phone-hang-up", ToastDismissDelay.Short);
            return;
        }

        if (callId is not null)
            NameOutgoing(chatId, callId);
        SystemCallUI.OnOutgoingCallStarted(chatId, callId, hasVideo);
    }

    public Task CancelCall(ChatId chatId, CancellationToken cancellationToken)
    {
        // Read before the release: the server must cancel the call this client held, not whichever
        // one the chat is in by the time the request lands.
        var callId = GetCallIdNonComputed(chatId);
        Release(chatId);
        SystemCallUI.OnOutgoingCallCancelled(chatId);
        return LiveSessions.CancelCall(Session, chatId, callId, cancellationToken);
    }

    public Task AcceptCall(ChatId chatId, CallId? callId, CancellationToken cancellationToken)
        => LiveSessions.AcceptCall(Session, chatId, ClientId, callId, cancellationToken);

    public Task DeclineCall(ChatId chatId, CallId? callId, CancellationToken cancellationToken)
        => LiveSessions.DeclineCall(Session, chatId, callId, cancellationToken);

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
        return StopCallAudio(chatId);
    }

    public async Task StopCallAudio(ChatId chatId)
    {
        await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(true);
        await ChatAudioUI.SetListeningState(chatId, false).ConfigureAwait(true);
    }

    public bool TryClaimOutgoing(ChatId chatId, AuthorId? peerId, bool hasVideo)
    {
        lock (_lock) {
            if (_activeCall.Value is not null)
                return false;

            SetIntentUnsafe(new ActiveCall(chatId, CallRole.Caller, CallPhase.Dialing, peerId, hasVideo));
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
            // Caller and video ride along from the ring when the slot already holds it; answering
            // before the projection lands leaves them unknown until the server's own answer does.
            SetIntentUnsafe(new ActiveCall(chatId, CallRole.Callee, CallPhase.Active,
                heldCall?.PeerId, heldCall?.HasVideo ?? false, callId ?? heldCall?.CallId));
        }

        return true;
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

    // Private methods

    private void NameOutgoing(ChatId chatId, CallId callId)
    {
        // The slot was claimed on the click, before the server had a call to name. Only that claim
        // takes the id: if GetMyCall got here first, the slot already has it.
        var isRejoined = false;
        lock (_lock) {
            if (_activeCall.Value is not { Role: CallRole.Caller, CallId: null } call || call.ChatId != chatId)
                return;

            call = call with { CallId = callId };
            if (_intent is { Call: not null } intent && intent.ChatId == chatId) {
                // Placed right after a hang-up, the call can be the very one just left - the server
                // joins a call that is still connected. It is wanted again, so it is no longer "left".
                isRejoined = intent.LeftCallId == callId;
                _intent = intent with { Call = call, LeftCallId = isRejoined ? null : intent.LeftCallId };
            }
            _activeCall.Value = call;
        }
        // The answers naming it were kept off the slot as the left call's; nothing else re-reads them.
        if (isRejoined)
            Touch();
    }

    // Caller must hold _lock.
    private void SetIntentUnsafe(ActiveCall call)
    {
        // The call just left rides along: the server may go on naming it past this new gesture.
        var leftCallId = CallIntentView.Of(_intent, Now, IntentGrace) is { IsFresh: true } last
            ? last.LeftCallId
            : null;
        _intent = new CallIntent(call, call.ChatId, Now, leftCallId);
        _activeCall.Value = call;
    }

    // Caller must hold _lock.
    private void ReleaseUnsafe(ChatId chatId)
    {
        // Recorded as an intent of its own: the server keeps naming this call mine until my absence
        // reaches it, and that answer must not put the screens back up.
        _intent = new CallIntent(null, chatId, Now, _activeCall.Value?.CallId);
        _activeCall.Value = null;
    }

    // Nested types

    // What this client last did to the slot, and when. A null Call means "I just left this chat's call";
    // LeftCallId names that call when the server had, so that it alone is kept off the slot.
    internal sealed record CallIntent(ActiveCall? Call, ChatId ChatId, Moment At, CallId? LeftCallId = null);
}

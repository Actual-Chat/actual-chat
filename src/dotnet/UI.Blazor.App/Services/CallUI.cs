using ActualChat.Localization;
using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
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
    private (bool IsCallActive, bool HasVideo) _reportedCallActivity;
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

        try {
            await LiveSessions.StartCall(Session, chatId, invitees, hasVideo, cancellationToken).ConfigureAwait(false);
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

        SystemCallUI.OnOutgoingCallStarted(chatId, hasVideo);
    }

    public Task CancelCall(ChatId chatId, CancellationToken cancellationToken)
    {
        Release(chatId);
        SystemCallUI.OnOutgoingCallCancelled(chatId);
        return LiveSessions.CancelCall(Session, chatId, cancellationToken);
    }

    public Task AcceptCall(ChatId chatId, CancellationToken cancellationToken)
        => LiveSessions.AcceptCall(Session, chatId, cancellationToken);

    public Task DeclineCall(ChatId chatId, CancellationToken cancellationToken)
        => LiveSessions.DeclineCall(Session, chatId, cancellationToken);

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

    public async Task HangUp(ChatId chatId)
    {
        // Leaving the call server-side follows from the stopped audio, through the same SetParticipation
        // path as any other presence change - see LiveSessionUI.RunParticipationSync. The server's claim
        // goes with that presence, on its next read of this call.
        Release(chatId);
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

        ReportCallActivity();
        return true;
    }

    public bool TryCommitAccept(ChatId chatId)
    {
        // From a free slot this claims it too: Answer on a notification can land before the projection does.
        lock (_lock) {
            var heldCall = _activeCall.Value;
            if (heldCall is not null && heldCall.ChatId != chatId)
                return false;

            // Caller and video ride along from the ring when the slot already holds it; answering
            // before the projection lands leaves them unknown until the server's own answer does.
            SetIntentUnsafe(new ActiveCall(chatId, CallRole.Callee, CallPhase.Active,
                heldCall?.PeerId, heldCall?.HasVideo ?? false));
        }

        ReportCallActivity();
        return true;
    }

    public bool DropRing(ChatId chatId)
    {
        // Reports whether the slot held the chat, in any phase. The slot goes only while the ring itself holds
        // it: the dismissal push our own accept triggers must not end the call it just started.
        lock (_lock) {
            if (_activeCall.Value is not { } call || call.ChatId != chatId)
                return false;

            if (call.Phase == CallPhase.Ringing)
                ReleaseUnsafe(chatId);
        }

        ReportCallActivity();
        return true;
    }

    public void Release(ChatId chatId)
    {
        lock (_lock) {
            if (_activeCall.Value?.ChatId == chatId)
                ReleaseUnsafe(chatId);
        }

        ReportCallActivity();
    }

    // Private methods

    // After every write to the slot, and outside _lock: the audio session's category follows this,
    // and a call on the line must keep the one that can reach the earpiece even while the mic is
    // off. Read from the slot rather than passed in, so a write that skipped Apply - an accept, a
    // placed call - is reported the same as the server's answer.
    private void ReportCallActivity()
    {
        (bool IsCallActive, bool HasVideo) activity;
        lock (_lock) {
            var call = _activeCall.Value;
            var isCallActive = call is { Phase: CallPhase.Active or CallPhase.Dialing };
            activity = (isCallActive, isCallActive && call!.HasVideo);
            if (activity == _reportedCallActivity)
                return;

            _reportedCallActivity = activity;
        }
        // The pick belongs to the call that just ended; the next one starts from the defaults.
        if (!activity.IsCallActive)
            _pickedOutputRouteId.Value = null;
        Hub.AudioFocusUI.SetCallActive(activity.IsCallActive, activity.HasVideo);
    }

    // Caller must hold _lock.
    private void SetIntentUnsafe(ActiveCall call)
    {
        _intent = new CallIntent(call, call.ChatId, Now);
        _activeCall.Value = call;
    }

    // Caller must hold _lock.
    private void ReleaseUnsafe(ChatId chatId)
    {
        // Recorded as an intent of its own: the server keeps naming this call mine until my absence
        // reaches it, and that answer must not put the screens back up.
        _intent = new CallIntent(null, chatId, Now);
        _activeCall.Value = null;
    }

    // Nested types

    // What this client last did to the slot, and when. A null Call means "I just left this chat's call".
    internal sealed record CallIntent(ActiveCall? Call, ChatId ChatId, Moment At);
}

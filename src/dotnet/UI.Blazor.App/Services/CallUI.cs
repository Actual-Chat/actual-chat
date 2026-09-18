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
    private CallIntent? _intent;

    private IIncomingCallsBridge? Bridge { get; }
    private ISystemCallUI SystemCallUI => field ??= Hub.Services.GetRequiredService<ISystemCallUI>();
    private ILiveSessions LiveSessions => Hub.LiveSessions;
    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
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
        // The slot is claimed before the StartCall RPC, but the outgoing screens read the invitee from the
        // session, and a refused call must not ring back first - so they wait for the server's dialing.
        var call = await GetActiveCall(cancellationToken).ConfigureAwait(false);
        if (call is not { Role: CallRole.Caller, Phase: CallPhase.Dialing })
            return null;

        var live = await LiveSessionUI.Get(call.ChatId, cancellationToken).ConfigureAwait(false);
        return live is { Kind: LiveSessionKind.Call, Conversation: null } ? call.ChatId : null;
    }

    [ComputeMethod]
    public virtual Task<CallerStatus?> GetCallStatus(ChatId chatId, CancellationToken cancellationToken)
        => LiveSessions.GetCallStatus(Session, chatId, cancellationToken);

    [ComputeMethod]
    public virtual async Task<IncomingCall?> GetRingingCall(ChatId chatId, CancellationToken cancellationToken)
    {
        // Straight from the session: this is what Accept re-verifies the ring against.
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
        // Only my own invite decides: the caller is never invited, someone else answering leaves mine Ringing,
        // and my own answer on another device moves it past Ringing.
        if (live is not { Kind: LiveSessionKind.Call })
            return null;

        var invite = live.Invites.FirstOrDefault(i => i.InviteeId == ownAuthorId);
        if (invite is not { Status: CallInviteStatus.Ringing })
            return null;

        return new IncomingCall(live.ChatId, live.Host, live.Rules.VideoAllowed);
    }

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
        // Taken before the RPC, so the screens follow the gesture rather than the round trip.
        if (!TryClaimOutgoing(chatId, hasVideo)) {
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

    public bool TryClaimOutgoing(ChatId chatId, bool hasVideo)
    {
        lock (_lock) {
            if (_activeCall.Value is not null)
                return false;

            SetIntentUnsafe(new ActiveCall(chatId, CallRole.Caller, CallPhase.Dialing, null, hasVideo));
            return true;
        }
    }

    public bool TryCommitAccept(IncomingCall call)
    {
        // From a free slot this claims it too: Answer on a notification can land before the projection does.
        var chatId = call.ChatId;
        lock (_lock) {
            if (_activeCall.Value is { } heldCall && heldCall.ChatId != chatId)
                return false;

            SetIntentUnsafe(new ActiveCall(chatId, CallRole.Callee, CallPhase.Active, call.Caller, call.HasVideo));
            return true;
        }
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
            return true;
        }
    }

    public void Release(ChatId chatId)
    {
        lock (_lock) {
            if (_activeCall.Value?.ChatId == chatId)
                ReleaseUnsafe(chatId);
        }
    }

    // Private methods

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

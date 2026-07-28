using ActualChat.Live;
using ActualChat.Notifications;
using ActualChat.UI.Blazor.App.Module;
using ActualLab.Diagnostics;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;
using Microsoft.JSInterop;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record IncomingCall(ChatId ChatId, AuthorId Caller, bool HasVideo);

/// <summary>
/// Client-side incoming-ring state: a push (or notification reconciliation) triggers
/// <see cref="OnRing"/>, but the reactive <see cref="LiveSessionUI.Get"/> is the source
/// of truth — a ring ends itself on cancel, timeout, decline, or accept on another device.
/// </summary>
public class IncomingCallUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    private static readonly string JSStartRingtone = $"{BlazorUIAppModule.ImportName}.IncomingCallRingtone.start";
    private static readonly string JSStopRingtone = $"{BlazorUIAppModule.ImportName}.IncomingCallRingtone.stop";

    private readonly Lock _ringingLock = new();
    private readonly MutableState<ImmutableList<ChatId>> _ringingChatIds;
    // The chat whose call surfaced over the lock screen; stays set through Accept (so the in-call
    // screen shows over the keyguard) until the user unlocks (GoToChat), hangs up, or the call ends.
    private readonly MutableState<ChatId?> _overLockChatId;
    // Held true across the Accept transition (ring ended, audio not yet started) so the over-lock
    // session doesn't momentarily read as ended and tear the screen down mid-accept.
    private readonly MutableState<bool> _isAccepting;
    private volatile bool _overLockWasActive;

    public IState<ChatId?> OverLockChatId => _overLockChatId;

    private IIncomingCallsBridge? Bridge { get; }
    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IAuthors Authors => Hub.Authors;
    private INotifications Notifications => Hub.Notifications;
    private ILogger? CallDebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.AndroidIncomingCalls);

    public IncomingCallUI(AppUIHub hub) : base(hub)
    {
        Bridge = hub.Services.GetService<IIncomingCallsBridge>();
        _ringingChatIds = StateFactory.NewMutable(
            ImmutableList<ChatId>.Empty,
            StateCategories.Get(GetType(), "RingingChatIds"));
        _overLockChatId = StateFactory.NewMutable(
            (ChatId?)null,
            StateCategories.Get(GetType(), "OverLockChatId"));
        _isAccepting = StateFactory.NewMutable(
            false,
            StateCategories.Get(GetType(), "IsAccepting"));
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    public void OnRing(ChatId chatId, bool showOverLockScreen = false)
    {
        if (chatId.Value.IsNullOrEmpty())
            return;

        CallDebugLog?.LogInformation("CALL_TRACE: OnRing #{ChatId}, showOverLockScreen={ShowOverLockScreen}", chatId, showOverLockScreen);
        lock (_ringingLock) {
            var chatIds = _ringingChatIds.Value;
            if (!chatIds.Contains(chatId))
                _ringingChatIds.Value = chatIds.Add(chatId);
        }
        if (showOverLockScreen) {
            _overLockWasActive = false;
            _overLockChatId.Value = chatId;
        }
    }

    // Called by the over-lock call screen after it has rendered. The render callback fires before the
    // WebView actually paints, so wait a beat before removing the native cover — otherwise the app's
    // restored route flashes through for a frame on a cold start.
    public void OnOverLockScreenRendered()
    {
        CallDebugLog?.LogInformation("CALL_TRACE: OnOverLockScreenRendered");
        _ = RevealCallScreenAfterPaint();
    }

    private async Task RevealCallScreenAfterPaint()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
        CallDebugLog?.LogInformation("CALL_TRACE: RevealCallScreen (after paint delay), Bridge={HasBridge}", Bridge is not null);
        Bridge?.RevealCallScreen();
    }

    public void OnCallDismissed(ChatId chatId)
    {
        if (chatId.Value.IsNullOrEmpty())
            return;

        EndRing(chatId);
        _ = Bridge?.OnCallHandled(false);
    }

    [ComputeMethod]
    public virtual async Task<IncomingCall?> GetIncomingCall(CancellationToken cancellationToken)
    {
        var chatIds = await _ringingChatIds.Use(cancellationToken).ConfigureAwait(false);
        for (var i = chatIds.Count - 1; i >= 0; i--) {
            var call = await GetRingingCall(chatIds[i], cancellationToken).ConfigureAwait(false);
            if (call is not null)
                return call;
        }
        return null;
    }

    [ComputeMethod]
    public virtual async Task<IncomingCall?> GetRingingCall(ChatId chatId, CancellationToken cancellationToken)
    {
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
        // An incoming ring is the Dialing phase; once someone answers it's promoted to Call and is
        // no longer an incoming call.
        if (live is not { Kind: LiveSessionKind.Dialing })
            return null;
        if (live.Host == ownAuthorId)
            return null;

        var invite = live.Invites.FirstOrDefault(i => i.InviteeId == ownAuthorId);
        if (invite is not { Status: CallInviteStatus.Ringing })
            return null;

        return new IncomingCall(live.ChatId, live.Host, live.Rules.VideoAllowed);
    }

    public async Task Accept(ChatId chatId)
    {
        var overLockScreen = _overLockChatId.Value == chatId;
        var call = await GetRingingCall(chatId, default).ConfigureAwait(true);
        EndRing(chatId);
        if (call is null) {
            _ = Bridge?.OnCallHandled(false);
            Hub.ToastUI.Show("Call ended", "icon-phone", ToastDismissDelay.Short);
            return;
        }

        // Hold the over-lock session "active" until audio starts, so the transition (ring ended,
        // recording not yet on) doesn't read as a session end and tear the screen down.
        if (overLockScreen)
            _isAccepting.Value = true;
        try {
            await LiveSessionUI.AcceptCall(chatId, default).ConfigureAwait(true);
        }
        catch (Exception e) {
            _isAccepting.Value = false;
            _ = Bridge?.OnCallHandled(false);
            Log.LogWarning(e, "AcceptCall failed for chat #{ChatId}", chatId);
            Hub.ToastUI.Show("Call ended", "icon-phone", ToastDismissDelay.Short);
            return;
        }

        // Anything failing past this point must still release _isAccepting: otherwise the over-lock
        // session reads as active forever and its call screen can never be torn down.
        try {
            // Accept over the lock screen keeps the call activity visible over the keyguard and starts
            // audio without unlocking: the mic FGS is allowed because the activity (shown via
            // SetShowWhenLocked) counts as foreground. Otherwise dismiss the keyguard first, since the
            // FGS can't start from a background state.
            var canStartAudio = overLockScreen
                || Bridge is null
                || await Bridge.OnCallHandled(true).ConfigureAwait(true);
            // Over the lock screen the chat opens only on go-to-chat (after PIN); the in-call screen
            // covers everything until then. Audio doesn't need the chat route — it's state-driven.
            if (!overLockScreen)
                await Hub.History.NavigateTo(Links.Chat(chatId)).ConfigureAwait(true);
            if (canStartAudio) {
                var micPermission = Hub.AudioRecorder.MicrophonePermission;
                if (await micPermission.CheckOrRequest(CancellationToken.None).ConfigureAwait(true))
                    await ChatAudioUI.SetRecordingChatId(chatId).ConfigureAwait(true);
                else
                    // Mic denied: still join the call as a listener.
                    await ChatAudioUI.SetListeningState(chatId, true).ConfigureAwait(true);
            }
        }
        finally {
            _isAccepting.Value = false;
        }
    }

    public async Task Decline(ChatId chatId)
    {
        var overLockScreen = _overLockChatId.Value == chatId;
        ClearOverLock();
        EndRing(chatId);
        if (overLockScreen)
            Bridge?.MoveBehindLockScreen();
        else
            _ = Bridge?.OnCallHandled(false);
        try {
            await LiveSessionUI.DeclineCall(chatId, default).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "DeclineCall failed for chat #{ChatId}", chatId);
        }
    }

    // From the over-lock in-call screen: dismiss the keyguard (PIN) and, once unlocked, close the
    // in-call screen and open the chat. On a cancelled PIN we stay on the in-call screen.
    public async Task GoToChat(ChatId chatId)
    {
        var unlocked = Bridge is null || await Bridge.OnCallHandled(true).ConfigureAwait(true);
        if (!unlocked)
            return;

        ClearOverLock();
        await Hub.History.NavigateTo(Links.Chat(chatId)).ConfigureAwait(true);
    }

    public async Task HangUp(ChatId chatId)
    {
        ClearOverLock();
        Bridge?.MoveBehindLockScreen();
        await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(true);
        await ChatAudioUI.SetListeningState(chatId, false).ConfigureAwait(true);
        try {
            await LiveSessionUI.LeaveCall(chatId, default).ConfigureAwait(true);
        }
        catch (Exception e) {
            Log.LogWarning(e, "LeaveCall failed for chat #{ChatId}", chatId);
        }
    }

    [ComputeMethod]
    protected virtual async Task<bool> IsOverLockSessionActive(CancellationToken cancellationToken)
    {
        var chatId = await _overLockChatId.Use(cancellationToken).ConfigureAwait(false);
        if (chatId is null)
            return false;

        if (await _isAccepting.Use(cancellationToken).ConfigureAwait(false))
            return true;

        if (await GetRingingCall(chatId, cancellationToken).ConfigureAwait(false) is not null)
            return true;

        var inCall = await LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken).ConfigureAwait(false);
        CallDebugLog?.LogInformation("CALL_TRACE: IsOverLockSessionActive #{ChatId} → inCall={InCall} (ring not confirmed)", chatId, inCall);
        return inCall;
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0.5, 10);
        return Task.WhenAll(
            AsyncChain.From(SyncRings)
                .Log(LogLevel.Debug, Log).RetryForever(retryDelays, Log).Run(cancellationToken),
            AsyncChain.From(SyncActiveCallNotifications)
                .Log(LogLevel.Debug, Log).RetryForever(retryDelays, Log).Run(cancellationToken),
            AsyncChain.From(ResetOverLockScreen)
                .Log(LogLevel.Debug, Log).RetryForever(retryDelays, Log).Run(cancellationToken));
    }

    // Private methods

    private async Task SyncRings(CancellationToken cancellationToken)
    {
        if (Bridge is not null)
            // A call push may have landed while the app was killed and the user opened it
            // from the launcher — pick the ring up from the still-active system notification.
            foreach (var chatId in await Bridge.ListActiveCallChatIds(cancellationToken).ConfigureAwait(false))
                OnRing(chatId);

        var cCall = await Computed
            .Capture(() => GetIncomingCall(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var isRinging = false;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var call = cCall.Value;
                if (call is not null != isRinging) {
                    isRinging = call is not null;
                    if (isRinging)
                        StartRinging();
                    else
                        StopRinging();
                }
                await PruneDeadRings(cancellationToken).ConfigureAwait(false);

                await cCall.WhenInvalidated(cancellationToken).ConfigureAwait(false);
                cCall = await cCall.Update(cancellationToken).ConfigureAwait(false);
            }
        }
        finally {
            if (isRinging)
                StopRinging();
        }
    }

    private void StartRinging()
    {
        // Routes the ring melody to the platform ringer: the native bridge on Android, the looping web
        // ringtone everywhere else. Fire-and-forget to mirror the sync Bridge calls (and keep the finally
        // teardown sync); the JS invocation swallows its own errors.
        if (Bridge is not null)
            Bridge.StartRinging();
        else
            _ = PlayWebRingtone(true);
    }

    private void StopRinging()
    {
        if (Bridge is not null)
            Bridge.StopRinging();
        else
            _ = PlayWebRingtone(false);
    }

    private async Task PlayWebRingtone(bool start)
    {
        try {
            await Hub.JS.InvokeVoidAsync(start ? JSStartRingtone : JSStopRingtone).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Web ringtone {Action} failed", start ? "start" : "stop");
        }
    }

    private async Task SyncActiveCallNotifications(CancellationToken cancellationToken)
    {
        // Android has its own incoming-call path (FCM push + a system call notification via the bridge),
        // so this reactive discovery is for every other platform only. The server's active-notification
        // set reactively carries this user's incoming-call rings, so a connected client discovers them
        // without waiting on a push. Seeds the same candidate set as OnRing; GetRingingCall +
        // PruneDeadRings confirm and prune against the live session.
        if (Bridge is not null)
            return;

        var cNotifications = await Computed
            .Capture(() => Notifications.ListActive(Session, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var c in cNotifications.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;

            foreach (var notification in c.Value)
                if (notification is CallNotification call)
                    OnRing(call.ChatId);
        }
    }

    private async Task PruneDeadRings(CancellationToken cancellationToken)
    {
        // Dead rings would otherwise accumulate for the whole scope lifetime; a still-live
        // second ring survives the prune and surfaces once the current one ends.
        ImmutableList<ChatId> chatIds;
        lock (_ringingLock)
            chatIds = _ringingChatIds.Value;
        foreach (var chatId in chatIds) {
            if (await GetRingingCall(chatId, cancellationToken).ConfigureAwait(false) is not null)
                continue;

            lock (_ringingLock)
                _ringingChatIds.Value = _ringingChatIds.Value.Remove(chatId);
        }
    }

    private async Task ResetOverLockScreen(CancellationToken cancellationToken)
    {
        // Tears down the over-lock screen once its session ends without the user unlocking (cancelled
        // ring, timeout, remote hang-up): closes the screen and sends the app behind the lock screen.
        // Guarded by _overLockWasActive so the cold-start load window (ring not yet confirmed) doesn't
        // fire early. The accept transition can't misfire here because _isAccepting keeps the session
        // active until audio starts. Direct exits (Decline/HangUp/GoToChat) clear the flag themselves.
        var cActive = await Computed
            .Capture(() => IsOverLockSessionActive(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested) {
            if (cActive.Value)
                _overLockWasActive = true;
            else if (_overLockWasActive && _overLockChatId.Value is not null) {
                CallDebugLog?.LogInformation("CALL_TRACE: ResetOverLockScreen teardown #{ChatId} (session ended, was active)", _overLockChatId.Value);
                ClearOverLock();
                Bridge?.MoveBehindLockScreen();
            }

            await cActive.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cActive = await cActive.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private void EndRing(ChatId chatId)
    {
        lock (_ringingLock) {
            var chatIds = _ringingChatIds.Value;
            if (chatIds.Contains(chatId))
                _ringingChatIds.Value = chatIds.Remove(chatId);
        }
        Bridge?.DismissCallNotification(chatId);
    }

    private void ClearOverLock()
    {
        _overLockWasActive = false;
        _isAccepting.Value = false;
        _overLockChatId.Value = null;
    }
}

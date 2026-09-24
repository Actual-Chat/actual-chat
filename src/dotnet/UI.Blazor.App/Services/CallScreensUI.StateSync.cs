using ActualChat.Live;
using ActualChat.UI.Blazor.Services;
namespace ActualChat.UI.Blazor.App.Services;

public partial class CallScreensUI
{
    // Outlives a restart of SyncCallView, so a restarted loop still tears down the call it last showed.
    private CallView _lastCallView = CallView.None;

    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(SyncRingtone),
            AsyncChain.From(SyncRingback),
            AsyncChain.From(SyncCallView),
            AsyncChain.From(SyncCallAudioRoute),
            AsyncChain.From(SyncExternalOutput),
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
        // An owned ring (CallKit) is the system's to silence, and stopping it ends the system call:
        // muting the in-app ring must not read as the ring being over.
        if (Bridge is { OwnsRinging: true })
            return true;

        var mutedChatId = await _mutedRingChatId.Use(cancellationToken).ConfigureAwait(false);
        return mutedChatId != call.ChatId;
    }

    [ComputeMethod]
    protected virtual async Task<CallAudioRoute?> GetActiveCallAudioRoute(CancellationToken cancellationToken)
    {
        var call = await CallUI.GetActiveCall(cancellationToken).ConfigureAwait(false);
        if (call is null || call.Phase == CallPhase.Ringing)
            return null;

        var choice = await _audioRouteChoice.Use(cancellationToken).ConfigureAwait(false);
        return choice?.ChatId == call.ChatId ? choice.Route : default(CallAudioRoute);
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
            // Teardown is not a ring end: this runs on scope disposal and on every fault the
            // RetryForever chain retries, so a bridge that owns the ring keeps it.
            if (isRinging)
                StopRinging(mustEndOwnedRing: false);
        }
    }

    private async Task SyncCallAudioRoute(CancellationToken cancellationToken)
    {
        var cRoute = await Computed
            .Capture(() => GetActiveCallAudioRoute(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var route = (CallAudioRoute?)null;
        try {
            await foreach (var c in cRoute.Changes(cancellationToken).ConfigureAwait(false)) {
                if (c.Value == route)
                    continue;

                route = c.Value;
                await Hub.AudioFocusUI.SetCallAudioRoute(route).ConfigureAwait(false);
            }
        }
        finally {
            // The call route must not outlive the call on a fault or scope disposal either.
            if (route is not null)
                await Hub.AudioFocusUI.SetCallAudioRoute(null).ConfigureAwait(false);
        }
    }

    private async Task SyncExternalOutput(CancellationToken cancellationToken)
    {
        var audioFocusUI = Hub.AudioFocusUI;
        audioFocusUI.OutputDevicesChanged += OnOutputDevicesChanged;
        try {
            OnOutputDevicesChanged();
            await TaskExt.NeverEnding(cancellationToken).ConfigureAwait(false);
        }
        finally {
            audioFocusUI.OutputDevicesChanged -= OnOutputDevicesChanged;
        }
        return;

        void OnOutputDevicesChanged() {
            var externalKind = audioFocusUI.GetExternalOutputKind();
            var lastExternalKind = _externalOutputKind.Value;
            _externalOutputKind.Value = externalKind;
            // A headset connected mid-call takes the audio over, as it does in the system dialer.
            if (externalKind is not null && externalKind != lastExternalKind
                && _audioRouteChoice.Value is { Route.IsBuiltinForced: true } choice)
                _audioRouteChoice.Value = choice with { Route = choice.Route with { IsBuiltinForced = false } };
        }
    }

    private async Task SyncCallView(CancellationToken cancellationToken)
    {
        var cView = await Computed
            .Capture(() => GetCallView(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var c in cView.Changes(cancellationToken).ConfigureAwait(false)) {
            // A failed read isn't a released slot: the last view stands until a good one arrives.
            if (c.HasError)
                continue;

            var last = _lastCallView;
            var view = c.Value;
            if (view == last)
                continue;

            // Remembered first: a teardown that throws must not rerun on every restart of the loop.
            _lastCallView = view;
            OnCallViewChanged(last, view);
        }
    }

    private void OnCallViewChanged(CallView last, CallView view)
    {
        if (last.Call is { } lastCall && lastCall.ChatId != view.Call?.ChatId)
            OnCallReleased(lastCall, last);
        if (view is not { Kind: CallViewKind.Modal, Call: { } call })
            return;

        // The modal closes itself once the view moves on, so it opens only on the switch to it.
        var wasModal = last.Kind == CallViewKind.Modal && last.Call?.ChatId == call.ChatId;
        if (!wasModal)
            ShowModal(new CallModal.Model(call.ChatId));
    }

    private void OnCallReleased(ActiveCall call, CallView last)
    {
        // The one place a call's screens are torn down, however the call ended.
        var chatId = call.ChatId;
        CallDebugLog?.LogInformation("CALL_TRACE: slot released #{ChatId} from {Phase}", chatId, call.Phase);
        ClearCallFlags(chatId);
        if (last.IsOverLock)
            Bridge?.MoveBehindLockScreen();
        var mustOpenChat = last is { Kind: CallViewKind.FullScreen, IsOverLock: false };
        if (call.Phase == CallPhase.Active || mustOpenChat)
            _ = Hub.Dispatcher.InvokeAsync(() => CloseCall(call, mustOpenChat));
    }

    private async Task CloseCall(ActiveCall call, bool mustOpenChat)
    {
        try {
            if (call.Phase == CallPhase.Active)
                await CallUI.HangUp(call.ChatId).ConfigureAwait(true);
            if (mustOpenChat)
                await OpenChat(call.ChatId).ConfigureAwait(true);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Closing the released call failed for chat #{ChatId}", call.ChatId);
        }
    }

    private void ClearCallFlags(ChatId chatId)
    {
        ClearIf(_collapsedChatId, chatId);
        ClearIf(_overLockRingChatId, chatId);
        ClearIf(_mutedRingChatId, chatId);
        if (_audioRouteChoice.Value?.ChatId == chatId)
            _audioRouteChoice.Value = null;
    }
}

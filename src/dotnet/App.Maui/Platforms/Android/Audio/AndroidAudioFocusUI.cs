using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Android.Media;

namespace ActualChat.App.Maui.Audio;

public sealed class AndroidAudioFocusUI : MauiAudioFocusUI
{
    private readonly AndroidAudioFocusHelper _focusHelper;
    private MauiAudioFocusHandle? _handle;
    private CarAudioRoute _carAudioRoute = CarAudioRoute.Default;
    private int _isTrackingCarAudioRoute;
    public override bool IsCommunicationFocus => _focusHelper.IsCommunicationFocus;

    public AndroidAudioFocusUI(AppUIHub hub)
        : base(hub)
    {
        _focusHelper = new AndroidAudioFocusHelper(Platform.AppContext, hub.LogFor<AndroidAudioFocusHelper>());
        _focusHelper.OnFocusChanged += OnFocusChanged;
        _focusHelper.OnOutputDevicesChanged += OnOutputDevicesChanged;
    }

    protected override Task DisposeAsyncCore()
    {
        _focusHelper.OnOutputDevicesChanged -= OnOutputDevicesChanged;
        _focusHelper.OnFocusChanged -= OnFocusChanged;
        _focusHelper.Dispose();
        return base.DisposeAsyncCore();
    }

    public override Task TryRecover(CancellationToken cancellationToken = default)
    {
        Log.LogInformation("TryRecover: attempting to recover audio focus");
        _handle?.RaiseFocusRecover();
        return Task.CompletedTask;
    }

    public override async Task EnsureOutputRoute(CancellationToken cancellationToken = default)
    {
        using var releaser = await OperationLock.Lock(cancellationToken).ConfigureAwait(false);
        releaser.MarkLockedLocally();
        await _focusHelper.EnsureCommunicationRoute(cancellationToken).ConfigureAwait(false);
    }

    public override async Task<AudioFocusScope?> TryAcquire(AudioFocusRequester requester)
    {
        // Hoisted above the OperationLock base takes: the lookup blocks on a content provider and,
        // on a cold cache, on an RPC - and an incoming ring waits on that same lock to pull the
        // ringtone out of the earpiece via YieldCommunicationMode.
        await UpdateCarAudioRoute().ConfigureAwait(false);
        return await base.TryAcquire(requester).ConfigureAwait(false);
    }

    public override async Task WarmUp()
    {
        var route = await UpdateCarAudioRoute().ConfigureAwait(false);
        using var releaser = await OperationLock.Lock(CancellationToken.None).ConfigureAwait(false);
        releaser.MarkLockedLocally();
        var isProjectionActive = route != CarAudioRoute.Default;
        await Task.Run(() => _focusHelper.WarmUpAudioMode(isProjectionActive), CancellationToken.None)
            .ConfigureAwait(false);
    }

    public override async Task YieldCommunicationMode()
    {
        using var releaser = await OperationLock.Lock(CancellationToken.None).ConfigureAwait(false);
        releaser.MarkLockedLocally();
        _focusHelper.YieldCommunicationMode();
    }

    public override async Task RestoreCommunicationMode()
    {
        using var releaser = await OperationLock.Lock(CancellationToken.None).ConfigureAwait(false);
        releaser.MarkLockedLocally();
        await _focusHelper.RestoreCommunicationMode().ConfigureAwait(false);
    }

    public override async Task EnsureBuiltinSpeakerRoute(CancellationToken cancellationToken = default)
    {
        using var releaser = await OperationLock.Lock(cancellationToken).ConfigureAwait(false);
        releaser.MarkLockedLocally();
        await _focusHelper.SelectBuiltinSpeaker(cancellationToken).ConfigureAwait(false);
    }

    // Protected/internal methods

    protected override async Task<MauiAudioFocusHandle?> RequestAudioFocus(AudioFocusMode mode)
    {
        // The route is read, never awaited, here: this runs under OperationLock.
        var carAudioRoute = Volatile.Read(ref _carAudioRoute);
        var kind = GetFocusRequestKind(mode, carAudioRoute);
        Log.LogInformation(
            "-> RequestAudioFocus, requested mode: '{Mode}', active handle: '{Handle}', "
            + "car route: {CarAudioRoute}, request: {Kind}",
            mode, _handle, carAudioRoute, kind);
        var success = await Task.Run(() => kind switch {
                FocusRequestKind.Call => _focusHelper.RequestFocusForCall(true),
                FocusRequestKind.ProjectedMedia => _focusHelper.RequestFocusForProjectedMedia(),
                FocusRequestKind.Playback => _focusHelper.RequestFocusForPlayback(),
                FocusRequestKind.Listening => _focusHelper.RequestFocusForListening(),
                FocusRequestKind.Notification =>
                    _focusHelper.RequestFocusForNotification(carAudioRoute != CarAudioRoute.Default),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported audio focus mode"),
            }, CancellationToken.None)
            .ConfigureAwait(false);
        if (!success) {
            Log.LogInformation("Failed to get audio focus");
            _handle = null;
            return null;
        }

        var handle = new MauiAudioFocusHandle(OnRelease);
        _handle = handle;
        Log.LogInformation("-- RequestAudioFocus: Success. Active handle: {Handle}, mode: {Mode}", handle, mode);
        return handle;

        void OnRelease(MauiAudioFocusHandle self) {
            Log.LogInformation("AudioFocusHandle {Handle} releasing", self);
            // ReSharper disable once AccessToModifiedClosure
            if (_handle == self) {
                _focusHelper.AbandonFocus();
                _handle = null;
            }
        }
    }

    // Private methods

    private static FocusRequestKind GetFocusRequestKind(AudioFocusMode mode, CarAudioRoute route)
    {
        // Under projection the projection link carries playback and the phone mic records, so
        // the communication route - an HFP virtual call the car answers by muting Android Auto -
        // is taken only when the user asked for the car microphone.
        var isProjecting = route != CarAudioRoute.Default;
        return mode switch {
            AudioFocusMode.Tune => FocusRequestKind.Notification,
            _ when route.UseCallLink => FocusRequestKind.Call,
            AudioFocusMode.Recording when isProjecting => FocusRequestKind.ProjectedMedia,
            AudioFocusMode.Listening when isProjecting => FocusRequestKind.ProjectedMedia,
            AudioFocusMode.Recording => FocusRequestKind.Call,
            AudioFocusMode.Playback => FocusRequestKind.Playback,
            AudioFocusMode.Listening => FocusRequestKind.Listening,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported audio focus mode"),
        };
    }

    private async Task<CarAudioRoute> UpdateCarAudioRoute()
    {
        EnsureCarAudioRouteTracking();
        var route = await Hub.ChatAudioUI.GetCarAudioRoute(CancellationToken.None).ConfigureAwait(false);
        // Published for RequestAudioFocus, which can't await it from under OperationLock.
        Volatile.Write(ref _carAudioRoute, route);
        return route;
    }

    private void EnsureCarAudioRouteTracking()
    {
        // Started lazily: the hub's ChatAudioUI is not resolvable from this constructor.
        if (Interlocked.Exchange(ref _isTrackingCarAudioRoute, 1) != 0)
            return;

        _ = AsyncChain.From(TrackCarAudioRoute)
            .RetryForever(RetryDelaySeq.Exp(1, 30), Log)
            .RunIsolated(Hub.StopToken);
    }

    private async Task TrackCarAudioRoute(CancellationToken cancellationToken)
    {
        // A renewal - a settings change mid-session, a mode switch between scopes - never passes
        // through TryAcquire, so it would keep the route the last acquire cached.
        var chatAudioUI = Hub.ChatAudioUI;
        var cRoute = await Computed
            .Capture(() => chatAudioUI.GetCarAudioRoute(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var lastRoute = Volatile.Read(ref _carAudioRoute);
        await foreach (var change in cRoute.Changes(cancellationToken).ConfigureAwait(false)) {
            var route = change.Value;
            if (route == lastRoute)
                continue;

            lastRoute = route;
            Volatile.Write(ref _carAudioRoute, route);
            // A held focus keeps the old link - SCO, the media channel - until something renews it,
            // which on a quiet chat is the next utterance: 17s of a muted car on 2026-09-09.
            // A track already playing keeps the usage it was built with, so an utterance caught
            // mid-flight finishes on the old link; the next one follows the new route.
            if (_handle is not null)
                await RenewHeldFocus().ConfigureAwait(false);
        }
    }

    private void OnFocusChanged(AudioFocus af)
    {
        Log.LogInformation("-> OnFocusChanged: {AudioFocus}. Active handle: {Handle}", af, _handle);
        if (_handle == null)
            return;

        if (af is AudioFocus.LossTransient or AudioFocus.LossTransientCanDuck)
            _handle.RaiseFocusLost(true, af is AudioFocus.LossTransientCanDuck);
        else if (af is AudioFocus.Loss)
            _handle.RaiseFocusLost(false, false);
        if (af is AudioFocus.Gain or AudioFocus.GainTransient or AudioFocus.GainTransientExclusive)
            _handle.RaiseFocusRecover();
    }

    private void OnOutputDevicesChanged()
    {
        // Note: Audio routing is now handled internally by AudioFocusHelper's device router
        // when devices change during active focus. This callback is kept for logging/monitoring.
        Log.LogInformation("-> OnOutputDevicesChanged. Active handle: {Handle}", _handle);
    }

    // Nested types

    private enum FocusRequestKind
    {
        Call,
        ProjectedMedia,
        Playback,
        Listening,
        Notification,
    }
}

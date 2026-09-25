using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Android.Media;

namespace ActualChat.App.Maui.Audio;

public sealed class AndroidAudioFocusUI : MauiAudioFocusUI
{
    // Android tells a headset from the rest by kind, never by device, so one route stands for it.
    private const string ExternalRouteId = "external";

    private readonly AndroidAudioFocusHelper _focusHelper;
    private readonly MutableState<AudioOutputRoutes> _outputRoutes;
    private MauiAudioFocusHandle? _handle;
    private CarAudioRoute _carAudioRoute = CarAudioRoute.Default;
    private int _isTrackingCarAudioRoute;
    private int _isCallActive;
    // Non-null while a call is on: all its audio then takes the communication route, and the route
    // only picks the device. A call's playback is one long track, so its usage can't follow a focus
    // change mid-call.
    private CallAudioRoute? _callAudioRoute;
    private AudioOutputKind? _lastExternalOutputKind;
    public override bool IsCommunicationFocus => _focusHelper.IsCommunicationFocus;
    // Nothing to pick from without an earpiece - a tablet - so the call screen shows no button there.
    public override IState<AudioOutputRoutes>? OutputRoutes => _focusHelper.HasEarpiece ? _outputRoutes : null;

    public AndroidAudioFocusUI(AppUIHub hub)
        : base(hub)
    {
        _focusHelper = new AndroidAudioFocusHelper(Platform.AppContext, hub.LogFor<AndroidAudioFocusHelper>());
        _outputRoutes = hub.StateFactory.NewMutable(
            AudioOutputRoutes.None, StateCategories.Get(GetType(), nameof(OutputRoutes)));
        _focusHelper.OnFocusChanged += OnFocusChanged;
        _focusHelper.OnOutputDevicesChanged += OnOutputDevicesChanged;
        _lastExternalOutputKind = _focusHelper.GetExternalOutputKind();
        RefreshOutputRoutes();
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

    public override void SetCallActive(bool isCallActive, bool hasVideo)
    {
        // Reconciled to the latest report rather than applied in order: two reports landing out of
        // order must not leave a finished call's route behind.
        Volatile.Write(ref _isCallActive, isCallActive ? 1 : 0);
        _ = BackgroundTask.Run(SyncCallAudioRoute, Log, "Failed to sync the call audio route", Hub.StopToken);
    }

    public override async Task SelectOutputRoute(string routeId)
    {
        if (_callAudioRoute is not { } route) {
            Log.LogWarning("SelectOutputRoute: no call is on, ignoring {RouteId}", routeId);
            return;
        }

        // Shown as picked before it is: the refresh after the switch corrects a pick that fails.
        var routes = _outputRoutes.Value;
        if (routes.Routes.Any(x => x.Id == routeId))
            _outputRoutes.Value = routes with { CurrentId = routeId };
        route = routeId switch {
            AudioOutputRoute.PhoneId => new CallAudioRoute(true, true),
            AudioOutputRoute.SpeakerId => new CallAudioRoute(false, true),
            _ => route with { IsBuiltinForced = false },
        };
        await SetCallAudioRoute(route).ConfigureAwait(false);
    }

    public override AudioOutputKind? GetCurrentOutputKind()
        => _focusHelper.GetCurrentOutputKind();

    // Protected/internal methods

    protected override async Task<MauiAudioFocusHandle?> RequestAudioFocus(AudioFocusMode mode)
    {
        // The route is read, never awaited, here: this runs under OperationLock.
        var carAudioRoute = Volatile.Read(ref _carAudioRoute);
        var kind = GetFocusRequestKind(mode, carAudioRoute, _callAudioRoute is not null);
        Log.LogInformation(
            "-> RequestAudioFocus, requested mode: '{Mode}', active handle: '{Handle}', "
            + "car route: {CarAudioRoute}, request: {Kind}",
            mode, _handle, carAudioRoute, kind);
        var success = await Task.Run(() => kind switch {
                FocusRequestKind.Call => _focusHelper.RequestFocusForCall(true),
                FocusRequestKind.AssistantLink => _focusHelper.RequestFocusForAssistantLink(),
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

    private Task SyncCallAudioRoute()
    {
        var isCallActive = Volatile.Read(ref _isCallActive) != 0;
        var route = isCallActive ? _callAudioRoute ?? default(CallAudioRoute) : (CallAudioRoute?)null;
        return SetCallAudioRoute(route);
    }

    private async Task SetCallAudioRoute(CallAudioRoute? route)
    {
        bool mustRenew;
        using (var releaser = await OperationLock.Lock(CancellationToken.None).ConfigureAwait(false)) {
            releaser.MarkLockedLocally();
            var lastRoute = _callAudioRoute;
            if (lastRoute == route)
                return;

            Log.LogInformation("SetCallAudioRoute: {Route}", route);
            _callAudioRoute = route;
            await _focusHelper.SetCallAudioRoute(route ?? default).ConfigureAwait(false);
            // Only a call starting or ending changes the focus kind; a pick within a call just moves the device.
            var carAudioRoute = Volatile.Read(ref _carAudioRoute);
            mustRenew = _handle is not null
                && GetFocusRequestKind(ActiveMode, carAudioRoute, lastRoute is not null)
                != GetFocusRequestKind(ActiveMode, carAudioRoute, route is not null);
        }
        if (mustRenew)
            await RenewHeldFocus().ConfigureAwait(false);
        RefreshOutputRoutes();
    }

    private void RefreshOutputRoutes()
    {
        // Built from the route we asked for, not read back: CommunicationDevice blocks in AudioService
        // for up to 3s while a route change is still landing.
        var externalKind = _focusHelper.GetExternalOutputKind();
        var route = _callAudioRoute ?? default;
        var routes = new List<AudioOutputRoute>(3);
        if (externalKind is { } kind)
            routes.Add(new AudioOutputRoute(ExternalRouteId, kind));
        if (_focusHelper.HasEarpiece)
            routes.Add(new AudioOutputRoute(AudioOutputRoute.PhoneId, AudioOutputKind.Phone));
        routes.Add(new AudioOutputRoute(AudioOutputRoute.SpeakerId, AudioOutputKind.Speaker));
        var currentId = externalKind is not null && !route.IsBuiltinForced ? ExternalRouteId
            : route.IsEarpiece ? AudioOutputRoute.PhoneId
            : AudioOutputRoute.SpeakerId;
        _outputRoutes.Value = new AudioOutputRoutes(routes, currentId);
    }

    private static FocusRequestKind GetFocusRequestKind(
        AudioFocusMode mode,
        CarAudioRoute route,
        bool isInCall)
    {
        // Under projection the projection link carries playback and the phone mic records, so
        // the communication route - an HFP virtual call the car answers by muting Android Auto -
        // is taken only when the user asked for the car microphone.
        var isProjecting = route != CarAudioRoute.Default;
        return mode switch {
            AudioFocusMode.Tune => FocusRequestKind.Notification,
            _ when route.UseAssistantLink => FocusRequestKind.AssistantLink,
            _ when route.UseCallLink => FocusRequestKind.Call,
            AudioFocusMode.Recording when isProjecting => FocusRequestKind.ProjectedMedia,
            AudioFocusMode.Listening when isProjecting => FocusRequestKind.ProjectedMedia,
            AudioFocusMode.Recording => FocusRequestKind.Call,
            AudioFocusMode.Listening or AudioFocusMode.Playback when isInCall => FocusRequestKind.Call,
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
        // Routing follows the change inside AudioFocusHelper's device router while a focus is held.
        // Raised on the main thread, and the device reads below block on AudioService.
        Log.LogInformation("-> OnOutputDevicesChanged. Active handle: {Handle}", _handle);
        _ = BackgroundTask.Run(HandleOutputDevicesChanged, Log, "Failed to handle an output device change",
            Hub.StopToken);
    }

    private async Task HandleOutputDevicesChanged()
    {
        var externalKind = _focusHelper.GetExternalOutputKind();
        var isNewlyConnected = externalKind is not null && externalKind != _lastExternalOutputKind;
        _lastExternalOutputKind = externalKind;
        // A headset connected mid-call takes the audio over, as it does in the system dialer.
        if (isNewlyConnected && _callAudioRoute is { IsBuiltinForced: true } route)
            await SetCallAudioRoute(route with { IsBuiltinForced = false }).ConfigureAwait(false);
        else
            RefreshOutputRoutes();
    }

    // Nested types

    private enum FocusRequestKind
    {
        Call,
        AssistantLink,
        ProjectedMedia,
        Playback,
        Listening,
        Notification,
    }
}

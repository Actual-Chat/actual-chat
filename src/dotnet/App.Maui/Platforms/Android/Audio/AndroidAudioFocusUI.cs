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
        Log.LogInformation("-> RequestAudioFocus, requested mode: '{Mode}', active handle: '{Handle}'", mode, _handle);

        // The route is read, never awaited, here: this runs under OperationLock.
        var useCommunicationRoute = mode != AudioFocusMode.Recording
            || Volatile.Read(ref _carAudioRoute).Input != AudioEndpoint.Builtin;
        var success = await Task.Run(() => mode switch {
                AudioFocusMode.Recording => _focusHelper.RequestFocusForCall(useCommunicationRoute),
                AudioFocusMode.Playback => _focusHelper.RequestFocusForPlayback(),
                AudioFocusMode.Tune => _focusHelper.RequestFocusForNotification(),
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

    private async Task<CarAudioRoute> UpdateCarAudioRoute()
    {
        var route = await Hub.ChatAudioUI.GetCarAudioRoute(CancellationToken.None).ConfigureAwait(false);
        // Published for RequestAudioFocus, which can't await it from under OperationLock.
        Volatile.Write(ref _carAudioRoute, route);
        return route;
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
}

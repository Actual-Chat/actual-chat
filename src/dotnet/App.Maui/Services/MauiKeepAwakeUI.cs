using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiKeepAwakeUI))]
public class MauiKeepAwakeUI(UIHub hub) : KeepAwakeUI(hub)
{
    private KeepWebViewAliveUI KeepWebViewAliveUI => field ??= Hub.Services.GetRequiredService<KeepWebViewAliveUI>();

    public override async ValueTask SetKeepAwake(bool mustKeepAwake)
    {
        await SetKeepDisplayAwake(mustKeepAwake).ConfigureAwait(false);
        KeepWebViewAliveUI.IsEnabled.Value = mustKeepAwake;
        if (mustKeepAwake)
            KeepWebViewAliveUI.Start();
    }

    private ValueTask SetKeepDisplayAwake(bool value)
        => OperatingSystem.IsAndroid()
            ? base.SetKeepAwake(value)
            : DispatchToMainThread(() => SetKeepDisplayAwakeInternal(value))
                .ToValueTask();

#if WINDOWS
    private bool _skipSetKeepScreenOn;
    private bool _comExceptionDetected;

    private void SetKeepDisplayAwakeInternal(bool value)
    {
        // NOTE: There is an issue on some builds of Windows https://github.com/microsoft/WindowsAppSDK/issues/3002
        // The code below does not solve the issue, but it prevents the Sentry log from being spammed with exceptions.
        if (_skipSetKeepScreenOn) {
            Log.LogInformation("SetKeepAwake({MustKeepAwake}) - skipped", value);
            return;
        }

        Log.LogInformation("SetKeepAwake({MustKeepAwake})", value);
        try {
            DeviceDisplay.Current.KeepScreenOn = value;
        }
        catch (COMException e) {
            _comExceptionDetected = true;
            Log.LogDebug(e, "Failed to set KeepScreenOn={MustKeepAwake}", value);
        }
        catch (InvalidOperationException e) {
            if (_comExceptionDetected)
                _skipSetKeepScreenOn = true;
            Log.LogDebug(e, "Failed to set KeepScreenOn={MustKeepAwake}", value);
        }
    }
#else
    private void SetKeepDisplayAwakeInternal(bool value)
    {
        Log.LogInformation("SetKeepAwake({MustKeepAwake})", value);
        DeviceDisplay.Current.KeepScreenOn = value;
    }
#endif

#if IOS
    // Written on the main thread only, like the switch it guards.
    private bool _isScreenOffAtEar;

    public override ValueTask SetScreenOffAtEar(bool isEnabled)
        => DispatchToMainThread(() => {
            if (_isScreenOffAtEar == isEnabled)
                return;

            Log.LogInformation("SetScreenOffAtEar({IsEnabled})", isEnabled);
            _isScreenOffAtEar = isEnabled;
            if (isEnabled)
                IosProximityMonitoring.Acquire();
            else
                IosProximityMonitoring.Release();
        }).ToValueTask();
#elif ANDROID
    private Android.OS.PowerManager.WakeLock? _screenOffAtEarLock;

    public override ValueTask SetScreenOffAtEar(bool isEnabled)
    {
        if (isEnabled == _screenOffAtEarLock is not null)
            return default;

        Log.LogInformation("SetScreenOffAtEar({IsEnabled})", isEnabled);
        if (!isEnabled) {
            // Like a phone call: a call that ends at the ear leaves the screen off until it's taken away.
            _screenOffAtEarLock!.Release(Android.OS.WakeLockFlags.ReleaseFlagWaitForNoProximity);
            _screenOffAtEarLock = null;
            return default;
        }

        var powerManager = Android.App.Application.Context.GetSystemService(Android.Content.Context.PowerService)
            as Android.OS.PowerManager;
        if (powerManager?.IsWakeLockLevelSupported((int)Android.OS.WakeLockFlags.ProximityScreenOff) != true) {
            Log.LogWarning("SetScreenOffAtEar: no proximity wake lock on this device");
            return default;
        }

        var wakeLock = powerManager.NewWakeLock(Android.OS.WakeLockFlags.ProximityScreenOff, "voxt:ScreenOffAtEar")!;
        wakeLock.SetReferenceCounted(false);
        wakeLock.Acquire();
        _screenOffAtEarLock = wakeLock;
        return default;
    }
#endif
}

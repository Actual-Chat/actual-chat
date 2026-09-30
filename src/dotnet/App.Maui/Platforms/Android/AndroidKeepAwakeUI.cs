using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using Android.OS;

namespace ActualChat.App.Maui;

[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AndroidKeepAwakeUI))]
public sealed class AndroidKeepAwakeUI(UIHub hub) : MauiKeepAwakeUI(hub)
{
    private PowerManager.WakeLock? _screenOffAtEarLock;

    public override ValueTask SetScreenOffAtEar(bool isEnabled)
    {
        if (isEnabled == _screenOffAtEarLock is not null)
            return default;

        Log.LogInformation("SetScreenOffAtEar({IsEnabled})", isEnabled);
        if (!isEnabled) {
            // Like a phone call: a call that ends at the ear leaves the screen off until it's taken away.
            _screenOffAtEarLock!.Release(WakeLockFlags.ReleaseFlagWaitForNoProximity);
            _screenOffAtEarLock = null;
            return default;
        }

        var powerManager = Android.App.Application.Context.GetSystemService(Android.Content.Context.PowerService)
            as PowerManager;
        if (powerManager?.IsWakeLockLevelSupported((int)WakeLockFlags.ProximityScreenOff) != true) {
            Log.LogWarning("SetScreenOffAtEar: no proximity wake lock on this device");
            return default;
        }

        var wakeLock = powerManager.NewWakeLock(WakeLockFlags.ProximityScreenOff, "voxt:ScreenOffAtEar")!;
        wakeLock.SetReferenceCounted(false);
        wakeLock.Acquire();
        _screenOffAtEarLock = wakeLock;
        return default;
    }

    // Protected/internal methods

    protected override ValueTask SetKeepDisplayAwake(bool value)
        => SetKeepDisplayAwakeViaWeb(value);
}

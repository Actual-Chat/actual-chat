using ActualChat.Hosting;
#if IOS || MACCATALYST
using Foundation;
#endif

namespace ActualChat.App.Maui.Services;

// Must be singleton!
public sealed class MauiThermalTracker : ThermalTracker
{
    private readonly MutableState<ThermalLevel> _level;

    public override IState<ThermalLevel> Level => _level;

    public MauiThermalTracker(IServiceProvider services)
    {
        _level = services.StateFactory().NewMutable(ThermalLevel.Nominal);
        StartTracking();
    }

#if IOS || MACCATALYST
    private NSObject? _observer;

    private void StartTracking()
    {
        _level.Value = ToThermalLevel(NSProcessInfo.ProcessInfo.ThermalState);
        _observer = NSProcessInfo.Notifications.ObserveThermalStateDidChange(
            (_, _) => _level.Value = ToThermalLevel(NSProcessInfo.ProcessInfo.ThermalState));
    }

    private static ThermalLevel ToThermalLevel(NSProcessInfoThermalState state)
        => state switch {
            NSProcessInfoThermalState.Nominal => ThermalLevel.Nominal,
            NSProcessInfoThermalState.Fair => ThermalLevel.Fair,
            NSProcessInfoThermalState.Serious => ThermalLevel.Serious,
            NSProcessInfoThermalState.Critical => ThermalLevel.Critical,
            _ => ThermalLevel.Nominal,
        };
#elif ANDROID
    private ThermalStatusListener? _listener;

    private void StartTracking()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            return;
        if (Android.App.Application.Context.GetSystemService(Android.Content.Context.PowerService)
            is not Android.OS.PowerManager powerManager)
            return;

        _level.Value = ToThermalLevel(powerManager.CurrentThermalStatus);
        _listener = new ThermalStatusListener(status => _level.Value = ToThermalLevel(status));
        powerManager.AddThermalStatusListener(_listener);
    }

    // Android has 7 bands (None…Shutdown); Severe and above all mean "act now".
    private static ThermalLevel ToThermalLevel(Android.OS.ThermalStatus status)
        => status switch {
            Android.OS.ThermalStatus.None => ThermalLevel.Nominal,
            Android.OS.ThermalStatus.Light => ThermalLevel.Fair,
            Android.OS.ThermalStatus.Moderate => ThermalLevel.Serious,
            _ => ThermalLevel.Critical,
        };

    private sealed class ThermalStatusListener(Action<Android.OS.ThermalStatus> onChange)
        : Java.Lang.Object, Android.OS.PowerManager.IOnThermalStatusChangedListener
    {
        public void OnThermalStatusChanged(Android.OS.ThermalStatus status)
            => onChange(status);
    }
#else
    private void StartTracking()
    { }
#endif
}

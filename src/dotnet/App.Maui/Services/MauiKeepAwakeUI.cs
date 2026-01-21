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
            : MainThread.InvokeOnMainThreadAsync(() => SetKeepDisplayAwakeInternal(value))
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
}

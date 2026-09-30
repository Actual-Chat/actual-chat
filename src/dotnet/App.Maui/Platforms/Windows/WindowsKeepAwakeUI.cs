using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;

namespace ActualChat.App.Maui;

[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WindowsKeepAwakeUI))]
public sealed class WindowsKeepAwakeUI(UIHub hub) : MauiKeepAwakeUI(hub)
{
    private bool _mustSkipKeepScreenOn;
    private bool _hasComException;

    // Protected/internal methods

    protected override void SetKeepScreenOn(bool value)
    {
        // NOTE: There is an issue on some builds of Windows https://github.com/microsoft/WindowsAppSDK/issues/3002
        // The code below does not solve the issue, but it prevents the Sentry log from being spammed with exceptions.
        if (_mustSkipKeepScreenOn) {
            Log.LogInformation("SetKeepAwake({MustKeepAwake}) - skipped", value);
            return;
        }

        Log.LogInformation("SetKeepAwake({MustKeepAwake})", value);
        try {
            DeviceDisplay.Current.KeepScreenOn = value;
        }
        catch (COMException e) {
            _hasComException = true;
            Log.LogDebug(e, "Failed to set KeepScreenOn={MustKeepAwake}", value);
        }
        catch (InvalidOperationException e) {
            if (_hasComException)
                _mustSkipKeepScreenOn = true;
            Log.LogDebug(e, "Failed to set KeepScreenOn={MustKeepAwake}", value);
        }
    }
}

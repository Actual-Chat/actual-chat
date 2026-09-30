using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;

namespace ActualChat.App.Maui;

[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IosKeepAwakeUI))]
public sealed class IosKeepAwakeUI(UIHub hub) : MauiKeepAwakeUI(hub)
{
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
}

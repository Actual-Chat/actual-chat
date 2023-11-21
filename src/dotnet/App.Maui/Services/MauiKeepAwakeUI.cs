using System.Diagnostics.CodeAnalysis;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiKeepAwakeUI))]
public class MauiKeepAwakeUI(IServiceProvider services) : KeepAwakeUI(services)
{
    public override ValueTask SetKeepAwake(bool value)
        => MainThread.InvokeOnMainThreadAsync(() => {
            Log.LogInformation("SetKeepAwake({MustKeepAwake})", value);
            DeviceDisplay.Current.KeepScreenOn = value;
        }).ToValueTask();
}

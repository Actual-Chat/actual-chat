using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiKeepAwakeUI))]
public class MauiKeepAwakeUI(UIHub hub) : KeepAwakeUI(hub)
{
    [field: AllowNull, MaybeNull]
    private KeepWebViewAliveUI KeepWebViewAliveUI => field ??= Hub.GetRequiredService<KeepWebViewAliveUI>();

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
            : MainThread.InvokeOnMainThreadAsync(() => {
                    Log.LogInformation("SetKeepAwake({MustKeepAwake})", value);
                    DeviceDisplay.Current.KeepScreenOn = value;
                })
                .ToValueTask();
}

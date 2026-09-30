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

    // Protected/internal methods

    protected virtual ValueTask SetKeepDisplayAwake(bool value)
        => DispatchToMainThread(() => SetKeepScreenOn(value)).ToValueTask();

    protected virtual void SetKeepScreenOn(bool value)
    {
        Log.LogInformation("SetKeepAwake({MustKeepAwake})", value);
        DeviceDisplay.Current.KeepScreenOn = value;
    }
}

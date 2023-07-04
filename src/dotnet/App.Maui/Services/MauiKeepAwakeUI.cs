using ActualChat.UI.Blazor.Services;
using Dispatcher = Microsoft.AspNetCore.Components.Dispatcher;

namespace ActualChat.App.Maui.Services;

public class MauiKeepAwakeUI : KeepAwakeUI
{
    private Dispatcher? _dispatcher;
    private Dispatcher Dispatcher => _dispatcher ??= Services.GetRequiredService<Dispatcher>();

    public MauiKeepAwakeUI(IServiceProvider services) : base(services)
    { }

    public override ValueTask SetKeepAwake(bool value)
        => Dispatcher.InvokeAsync(() => SetKeepAwakeUnsafe(value)).ToValueTask();

    private void SetKeepAwakeUnsafe(bool value)
    {
        Log.LogDebug("SetKeepAwake({MustKeepAwake})", value);
        DeviceDisplay.Current.KeepScreenOn = value;
    }
}

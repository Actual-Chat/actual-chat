using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

public sealed class MacWindowUI(AppUIHub hub) : WindowUI(hub)
{
    protected override void SetInsetWindowButtons(bool mustInset)
        => BeginDispatchToMainThread(() => WindowConfigurator.SetInsetTrafficLights(mustInset));
}

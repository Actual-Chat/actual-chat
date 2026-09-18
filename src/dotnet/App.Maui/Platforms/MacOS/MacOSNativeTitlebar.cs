using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

public sealed class MacOSNativeTitlebar : INativeTitlebar
{
    public void SetInsetWindowButtons(bool mustInset)
        => BeginDispatchToMainThread(() => WindowConfigurator.SetInsetTrafficLights(mustInset));
}

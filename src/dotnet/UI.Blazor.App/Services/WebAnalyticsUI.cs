using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class WebAnalyticsUI(IServiceProvider services) : IAnalyticsUI
{
    private BrowserInit BrowserInit { get; } = services.GetRequiredService<BrowserInit>();

    public Task UpdateAnalyticsState(bool isEnabled)
        => BrowserInit.InitFirebase(isEnabled);
}

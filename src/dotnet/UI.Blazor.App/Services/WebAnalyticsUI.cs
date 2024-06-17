using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class WebAnalyticsUI(IServiceProvider services) : IAnalyticsUI
{
    private BrowserInit BrowserInit { get; } = services.GetRequiredService<BrowserInit>();

    public Task<bool> IsConfigured(CancellationToken cancellationToken)
        => BrowserInit.IsFirebaseConfigured();

    public Task UpdateAnalyticsState(bool isEnabled, CancellationToken cancellationToken)
        => BrowserInit.InitFirebase(isEnabled);
}

using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class WebDataCollectionSettingsUI(IServiceProvider services) : IDataCollectionSettingsUI
{
    private BrowserInit BrowserInit { get; } = services.GetRequiredService<BrowserInit>();

    public Task<bool> IsConfigured(CancellationToken cancellationToken)
        => BrowserInit.IsFirebaseConfigured();

    public Task UpdateState(bool isEnabled, CancellationToken cancellationToken)
        => BrowserInit.InitFirebase(isEnabled);
}

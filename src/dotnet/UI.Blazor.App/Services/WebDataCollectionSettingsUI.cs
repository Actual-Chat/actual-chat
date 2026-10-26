using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class WebDataCollectionSettingsUI(IServiceProvider services) : IDataCollectionSettingsUI
{
    private BrowserInit BrowserInit { get; } = services.GetRequiredService<BrowserInit>();

    public Task<bool> IsConfigured(CancellationToken cancellationToken)
        => BrowserInit.IsFirebaseConfigured();

    public Task UpdateState(bool isEnabled, CancellationToken cancellationToken)
        => BrowserInit.InitFirebase(isEnabled);

    public async Task<(AnalyticsClientKind Kind, string Id)?> GetAnalyticsClientId(CancellationToken cancellationToken)
    {
        var clientId = await BrowserInit.GetAnalyticsClientId().ConfigureAwait(false);
        return clientId.IsNullOrEmpty() ? null : (AnalyticsClientKind.Web, clientId);
    }
}

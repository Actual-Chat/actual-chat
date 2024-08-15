using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App;

public class WebDeviceTokenRetriever(IServiceProvider services) : IDeviceTokenRetriever
{
    private static readonly string JSGetDeviceTokenMethod =
        $"{BlazorUIAppModule.ImportName}.NotificationUI.getDeviceToken";

    private static readonly string JSDeleteDeviceTokenMethod =
        $"{BlazorUIAppModule.ImportName}.NotificationUI.deleteDeviceToken";

    private IJSRuntime JS { get; } = services.GetRequiredService<IJSRuntime>();

    public Task<string?> GetDeviceToken(CancellationToken cancellationToken)
        => JS.InvokeAsync<string?>(JSGetDeviceTokenMethod, CancellationToken.None)
            .AsTask().WaitAsync(cancellationToken);

    public Task DeleteDeviceToken(CancellationToken cancellationToken)
        => JS.InvokeVoidAsync(JSDeleteDeviceTokenMethod, CancellationToken.None)
            .AsTask().WaitAsync(cancellationToken);
}

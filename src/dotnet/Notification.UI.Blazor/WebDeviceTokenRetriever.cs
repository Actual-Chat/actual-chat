using ActualChat.Notification.UI.Blazor.Module;

namespace ActualChat.Notification.UI.Blazor;

public class WebDeviceTokenRetriever(IServiceProvider services) : IDeviceTokenRetriever
{
    private static readonly string JSGetDeviceTokenMethod =
        $"{NotificationBlazorUIModule.ImportName}.NotificationUI.getDeviceToken";

    private IJSRuntime JS { get; } = services.GetRequiredService<IJSRuntime>();

    public async Task<string?> GetDeviceToken(CancellationToken cancellationToken)
        => await JS.InvokeAsync<string?>(JSGetDeviceTokenMethod, cancellationToken).ConfigureAwait(false);
}

using ActualChat.Notification.UI.Blazor.Module;

namespace ActualChat.Notification.UI.Blazor;

public class WebDeviceTokenRetriever : IDeviceTokenRetriever
{
    private IJSRuntime JS { get; }

    public WebDeviceTokenRetriever(IJSRuntime js)
        => JS = js;

    public async Task<string?> GetDeviceToken(CancellationToken cancellationToken)
        => await JS.InvokeAsync<string?>($"{NotificationBlazorUIModule.ImportName}.NotificationUI.getDeviceToken", cancellationToken);
}

using ActualChat.Notification.UI.Blazor.Module;

namespace ActualChat.Notification.UI.Blazor;

public class WebDeviceTokenRetriever : IDeviceTokenRetriever
{
    private readonly IJSRuntime _js;

    public WebDeviceTokenRetriever(IJSRuntime js)
    => _js = js;

    public async Task<string?> GetDeviceToken(CancellationToken cancellationToken)
        => await _js.InvokeAsync<string?>($"{NotificationBlazorUIModule.ImportName}.NotificationUI.getDeviceToken", cancellationToken);
}

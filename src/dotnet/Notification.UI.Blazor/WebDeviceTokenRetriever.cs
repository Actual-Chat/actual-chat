using ActualChat.Notification.UI.Blazor.Module;

namespace ActualChat.Notification.UI.Blazor;

public class WebDeviceTokenRetriever : IDeviceTokenRetriever
{
    private static readonly string JSGetDeviceTokenMethod =
        $"{NotificationBlazorUIModule.ImportName}.NotificationUI.getDeviceToken";

    private IJSRuntime JS { get; }

    public WebDeviceTokenRetriever(IJSRuntime js)
        => JS = js;

    public async Task<string?> GetDeviceToken(CancellationToken cancellationToken)
        => await JS.InvokeAsync<string?>(JSGetDeviceTokenMethod, cancellationToken);
}

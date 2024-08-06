using ActualChat.Notification.UI.Blazor.Module;

namespace ActualChat.Notification.UI.Blazor;

public class WebDeviceTokenRetriever(IServiceProvider services) : IDeviceTokenRetriever
{
    private static readonly string JSGetDeviceTokenMethod =
        $"{NotificationBlazorUIModule.ImportName}.NotificationUI.getDeviceToken";

    private static readonly string JSDeleteDeviceTokenMethod =
        $"{NotificationBlazorUIModule.ImportName}.NotificationUI.deleteDeviceToken";

    private IJSRuntime JS { get; } = services.GetRequiredService<IJSRuntime>();

    public Task<string?> GetDeviceToken(CancellationToken cancellationToken)
        => JS.InvokeAsync<string?>(JSGetDeviceTokenMethod, CancellationToken.None)
            .AsTask().WaitAsync(cancellationToken);

    public Task DeleteDeviceToken(CancellationToken cancellationToken)
        => JS.InvokeVoidAsync(JSDeleteDeviceTokenMethod, CancellationToken.None)
            .AsTask().WaitAsync(cancellationToken);
}

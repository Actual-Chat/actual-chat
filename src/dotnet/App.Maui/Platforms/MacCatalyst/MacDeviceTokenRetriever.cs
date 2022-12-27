using ActualChat.Notification.UI.Blazor;

namespace ActualChat.App.Maui;

public class MacDeviceTokenRetriever : IDeviceTokenRetriever
{
    public Task<string?> GetDeviceToken(CancellationToken cancellationToken)
        => Task.FromResult((string?)null);
}

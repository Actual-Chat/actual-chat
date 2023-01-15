using ActualChat.Notification.UI.Blazor;

namespace ActualChat.App.Maui;

public class WindowsDeviceTokenRetriever : IDeviceTokenRetriever
{
    public Task<string?> GetDeviceToken(CancellationToken cancellationToken)
        => Task.FromResult((string?)null);
}

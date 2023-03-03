using ActualChat.Notification.UI.Blazor;

namespace ActualChat.App.Maui;

internal class IosDeviceTokenRetriever : IDeviceTokenRetriever
{
    public Task<string?> GetDeviceToken(CancellationToken cancellationToken)
        => Task.FromResult<string?>(null); // TODO: subscribe for notifications
}

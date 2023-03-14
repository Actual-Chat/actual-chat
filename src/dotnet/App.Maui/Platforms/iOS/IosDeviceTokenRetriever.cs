using ActualChat.Notification.UI.Blazor;

namespace ActualChat.App.Maui;

internal class IosDeviceTokenRetriever : IDeviceTokenRetriever
{
    private ILogger Log { get; }

    public IosDeviceTokenRetriever(IServiceProvider services)
        => Log = services.LogFor<IosDeviceTokenRetriever>();

    public Task<string?> GetDeviceToken(CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);
}

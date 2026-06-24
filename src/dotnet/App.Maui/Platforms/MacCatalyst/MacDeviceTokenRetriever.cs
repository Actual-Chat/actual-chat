using ActualChat.UI.Blazor.App;

namespace ActualChat.App.Maui;

public class MacDeviceTokenRetriever : IDeviceTokenRetriever
{
    public async Task<string?> GetDeviceToken(CancellationToken cancellationToken)
        => await Firebase.CloudMessaging.Messaging.SharedInstance.FetchTokenAsync().ConfigureAwait(false);

    public Task DeleteDeviceToken(CancellationToken cancellationToken)
        => Task.CompletedTask;
}

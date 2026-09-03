using ActualChat.UI.Blazor.App;

namespace ActualChat.App.Maui;

public class MacDeviceTokenRetriever : IDeviceTokenRetriever
{
    public Task<string?> GetDeviceToken(CancellationToken cancellationToken)
        => Task.FromResult((string?)null);

    public Task DeleteDeviceToken(CancellationToken cancellationToken)
        => Task.CompletedTask;
}

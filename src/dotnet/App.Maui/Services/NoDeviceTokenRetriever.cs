using ActualChat.UI.Blazor.App;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// <see cref="IDeviceTokenRetriever"/> for the platforms without a push channel:
/// Mac Catalyst, AppKit and Linux.
/// </summary>
public sealed class NoDeviceTokenRetriever : IDeviceTokenRetriever
{
    public Task<string?> GetDeviceToken(CancellationToken cancellationToken)
        => Task.FromResult((string?)null);

    public Task DeleteDeviceToken(CancellationToken cancellationToken)
        => Task.CompletedTask;
}

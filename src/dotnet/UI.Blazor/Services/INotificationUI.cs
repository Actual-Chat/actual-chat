namespace ActualChat.UI.Blazor.Services;

public interface INotificationUI
{
    Task UnregisterDevice(CancellationToken cancellationToken = default);
    Task EnsureDeviceRegistered(CancellationToken cancellationToken = default);
}

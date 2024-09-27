namespace ActualChat.UI.Blazor.Services;

public interface INotificationUI
{
    Task DeregisterDevice(CancellationToken cancellationToken = default);
    Task EnsureDeviceRegistered(CancellationToken cancellationToken = default);
    Task NavigateToNotificationUrl(string url);
}

namespace ActualChat.Notification.UI.Blazor;

public interface INotificationPermissions
{
    Task<PermissionState> GetNotificationPermissionState(CancellationToken cancellationToken);
    Task RequestNotificationPermissions(CancellationToken cancellationToken);
}

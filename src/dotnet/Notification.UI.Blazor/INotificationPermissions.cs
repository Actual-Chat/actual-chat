namespace ActualChat.Notification.UI.Blazor;

public interface INotificationsPermission
{
    Task<PermissionState> GetPermissionState(CancellationToken cancellationToken);
    Task RequestNotificationPermission(CancellationToken cancellationToken);
}

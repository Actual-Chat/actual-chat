namespace ActualChat.Notification.UI.Blazor;

public interface INotificationPermissions
{
    Task<PermissionState> GetPermissionState(CancellationToken cancellationToken);
    Task RequestNotificationPermission(CancellationToken cancellationToken);
}

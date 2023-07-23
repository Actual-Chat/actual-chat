namespace ActualChat.Notification.UI.Blazor;

public interface INotificationUIBackend
{
    [JSInvokable]
    Task HandleNotificationNavigation(string absoluteUrl);
    [JSInvokable]
    void SetPermissionState(string permissionState);
}

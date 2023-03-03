namespace ActualChat.Notification.UI.Blazor;

public interface INotificationUIBackend
{
    [JSInvokable]
    Task HandleNotificationNavigation(string url);

    [JSInvokable]
    Task UpdateNotificationStatus(string permissionState);
}

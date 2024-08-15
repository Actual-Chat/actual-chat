namespace ActualChat.UI.Blazor.App;

public interface INotificationUIBackend
{
    [JSInvokable]
    Task HandleNotificationNavigation(string absoluteUrl);
    [JSInvokable]
    void SetPermissionState(string permissionState);
}

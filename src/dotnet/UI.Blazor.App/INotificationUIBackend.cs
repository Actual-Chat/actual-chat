namespace ActualChat.UI.Blazor.App;

public interface INotificationUIBackend
{
    [JSInvokable]
    Task NavigateToNotificationUrl(string absoluteUrl);
    [JSInvokable]
    void SetPermissionState(string permissionState);
}

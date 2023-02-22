using ActualChat.Notification.UI.Blazor;

namespace ActualChat.App.Maui;

public class WindowsNotificationPermissions : INotificationPermissions
{
    public WindowsNotificationPermissions()
    { }

    public Task<PermissionState> GetNotificationPermissionState(CancellationToken cancellationToken)
        => Task.FromResult(PermissionState.Denied);

    public Task RequestNotificationPermissions(CancellationToken cancellationToken)
        => Task.CompletedTask;
}

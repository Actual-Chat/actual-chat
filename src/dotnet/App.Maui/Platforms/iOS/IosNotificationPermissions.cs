using ActualChat.Notification.UI.Blazor;

namespace ActualChat.App.Maui;

public class IosNotificationPermissions : INotificationPermissions
{
    public IosNotificationPermissions()
    { }

    public Task<PermissionState> GetNotificationPermissionState(CancellationToken cancellationToken)
        => Task.FromResult(PermissionState.Denied);

    public Task RequestNotificationPermissions(CancellationToken cancellationToken)
        => Task.CompletedTask;
}

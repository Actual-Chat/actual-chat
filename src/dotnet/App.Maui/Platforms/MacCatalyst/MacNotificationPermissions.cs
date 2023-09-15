using ActualChat.Notification.UI.Blazor;
using ActualChat.UI.Blazor;

namespace ActualChat.App.Maui;

public class MacNotificationPermissions : INotificationPermissions
{
    public MacNotificationPermissions()
    { }

    public Task<PermissionState> GetPermissionState(CancellationToken cancellationToken)
        => Task.FromResult(PermissionState.Denied);

    public Task RequestNotificationPermission(CancellationToken cancellationToken)
        => Task.CompletedTask;
}

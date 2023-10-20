using ActualChat.Notification.UI.Blazor;

namespace ActualChat.App.Maui;

public class WindowsNotificationsPermission : INotificationsPermission
{
    public Task<bool?> IsGranted(CancellationToken cancellationToken = default)
        => Task.FromResult((bool?)false);

    public Task Request(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

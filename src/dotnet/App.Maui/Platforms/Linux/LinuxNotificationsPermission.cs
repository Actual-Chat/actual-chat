using ActualChat.UI.Blazor.App;

namespace ActualChat.App.Maui;

/// <summary>
/// <see cref="INotificationsPermission"/> for the GTK backend: a Linux desktop shows an app's
/// notifications without asking, so there is nothing to request.
/// </summary>
public sealed class LinuxNotificationsPermission : INotificationsPermission
{
    public Task<bool?> IsGranted(CancellationToken cancellationToken = default)
        => Task.FromResult<bool?>(true);

    public Task Request(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

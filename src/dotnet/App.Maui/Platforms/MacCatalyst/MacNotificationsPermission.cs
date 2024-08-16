using ActualChat.UI.Blazor.App;

namespace ActualChat.App.Maui;

public class MacNotificationsPermission : INotificationsPermission
{
    public Task<bool?> IsGranted(CancellationToken cancellationToken = default)
        => Task.FromResult((bool?)false);

    public Task Request(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

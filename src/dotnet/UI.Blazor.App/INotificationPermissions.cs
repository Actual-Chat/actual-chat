namespace ActualChat.UI.Blazor.App;

public interface INotificationsPermission
{
    // null = undetermined / never requested
    Task<bool?> IsGranted(CancellationToken cancellationToken = default);
    Task Request(CancellationToken cancellationToken = default);
}

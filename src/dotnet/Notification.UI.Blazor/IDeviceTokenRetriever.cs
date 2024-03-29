namespace ActualChat.Notification.UI.Blazor;

public interface IDeviceTokenRetriever
{
    public Task<string?> GetDeviceToken(CancellationToken cancellationToken);
}

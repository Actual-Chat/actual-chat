namespace ActualChat.UI.Blazor.App;

public interface IDeviceTokenRetriever
{
    public Task<string?> GetDeviceToken(CancellationToken cancellationToken);
    public Task DeleteDeviceToken(CancellationToken cancellationToken);
}

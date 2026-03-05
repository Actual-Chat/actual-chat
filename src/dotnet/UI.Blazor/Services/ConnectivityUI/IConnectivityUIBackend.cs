namespace ActualChat.UI.Blazor.Services;

public interface IConnectivityUIBackend
{
    [JSInvokable]
    void OnOnlineChanged(bool isOnline);
}

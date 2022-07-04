namespace ActualChat.UI.Blazor.Layouts;

public interface IBaseLayoutBackend
{
    [JSInvokable]
    public Task HandleNotificationNavigation(string url);
}

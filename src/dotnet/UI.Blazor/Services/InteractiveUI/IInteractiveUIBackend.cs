namespace ActualChat.UI.Blazor.Services;

public interface IInteractiveUIBackend
{
    Task IsInteractiveChanged(bool value);
    Task Demand(string operation);
}

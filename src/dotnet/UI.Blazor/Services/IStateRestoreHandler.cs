namespace ActualChat.UI.Blazor.Services;

public interface IStateRestoreHandler
{
    double Priority { get; }
    Task Restore();
}

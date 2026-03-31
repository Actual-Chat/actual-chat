namespace ActualChat.UI.Blazor.App.Services;

public interface IMauiLogAccessor
{
    string ActionName { get; }
    Func<Task>? GetLogFile { get; }
}

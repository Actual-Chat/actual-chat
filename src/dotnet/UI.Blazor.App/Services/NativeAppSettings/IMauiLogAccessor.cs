namespace ActualChat.UI.Blazor.App.Services;

public interface IMauiLogAccessor
{
    string ActionName { get; }
    Func<Task<bool>>? GetLogFile { get; }
}

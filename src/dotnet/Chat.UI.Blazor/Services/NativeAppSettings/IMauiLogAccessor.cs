namespace ActualChat.Chat.UI.Blazor.Services;

public interface IMauiLogAccessor
{
    string ActionName { get; }
    Func<Task<bool>>? GetLogFile { get; }
}

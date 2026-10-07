namespace ActualChat.UI.Blazor.App.Services;

public interface IMauiLogAccessor
{
    string ActionName { get; }
    Func<Task>? GetLogFile { get; }
    // Null where the file log can't be switched off
    bool? IsFileLogEnabled { get; }

    void SetFileLogEnabled(bool isEnabled);
}

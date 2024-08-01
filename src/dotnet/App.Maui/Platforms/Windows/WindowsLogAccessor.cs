using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

public class WindowsLogAccessor : IMauiLogAccessor
{
    private readonly ILogger _log;

    public WindowsLogAccessor(ILogger<WindowsLogAccessor> log)
    {
        _log = log;
        if (!MauiDiagnostics.LogFilePath.IsNullOrEmpty())
            GetLogFile = OpenLogFileInternal;
    }

 #pragma warning disable CA1822
    public string ActionName => "Open log file";
 #pragma warning restore CA1822

    public Func<Task<bool>>? GetLogFile { get; }

    private Task<bool> OpenLogFileInternal()
    {
        try {
            var started = new Process {
                StartInfo = new ProcessStartInfo(MauiDiagnostics.LogFilePath) {
                    UseShellExecute = true
                },
            }.Start();
            if (started)
                return Task.FromResult(true);
        }
        catch(Exception e) {
            _log.LogWarning(e, "Failed to open log file '{FilePath}'",
                MauiDiagnostics.LogFilePath);
        }
        return Task.FromResult(false);
    }
}

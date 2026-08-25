using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualLab.Fusion.UI;

namespace ActualChat.App.Maui;

public sealed class WindowsLogAccessor : IMauiLogAccessor
{
    private readonly IServiceProvider _services;
    private readonly ILogger _log;

    private ToastUI ToastUI => field ??= _services.GetRequiredService<ToastUI>();
    private UICommander UICommander => field ??= _services.GetRequiredService<UICommander>();

    public WindowsLogAccessor(IServiceProvider services)
    {
        _services = services;
        _log = services.LogFor(GetType());
        if (!MauiDiagnostics.AppDataLogFilePath.IsEmpty)
            GetLogFile = OpenLogFileInternal;
    }

#pragma warning disable CA1822
    public string ActionName => "Open log file";
#pragma warning restore CA1822

    public Func<Task>? GetLogFile { get; }

    private Task OpenLogFileInternal()
    {
        var filePath = GetCurrentLogFilePath();
        try {
            var started = new Process {
                StartInfo = new ProcessStartInfo(filePath) {
                    UseShellExecute = true,
                },
            }.Start();
            if (started) {
                ToastUI.Show("Got log file successfully.", "icon-checkmark-circle-2", ToastDismissDelay.Short);
                return Task.CompletedTask;
            }
        }
        catch (Exception e) {
            _log.LogWarning(e, "Failed to open log file: {FilePath}", filePath);
        }

        UICommander.ShowError(StandardError.Constraint("Failed to get log file."));
        return Task.CompletedTask;
    }

    private string GetCurrentLogFilePath()
    {
        // The sink rolls on size, so the file being written to is the newest
        // "ActualChat*.log" rather than AppDataLogFilePath itself.
        var basePath = MauiDiagnostics.AppDataLogFilePath;
        try {
            var pattern = basePath.FileNameWithoutExtension.Value + "*" + basePath.Extension;
            var newestFile = new DirectoryInfo(basePath.DirectoryPath.Value)
                .EnumerateFiles(pattern)
                .MaxBy(x => x.LastWriteTimeUtc);
            return newestFile?.FullName ?? basePath.Value;
        }
        catch (Exception e) {
            _log.LogWarning(e, "Failed to find the current log file in {Folder}", basePath.DirectoryPath.Value);
            return basePath.Value;
        }
    }
}

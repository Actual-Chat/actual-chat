using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualLab.Fusion.UI;

namespace ActualChat.App.Maui;

public class WindowsLogAccessor : IMauiLogAccessor
{
    private readonly IServiceProvider _services;
    private readonly ILogger _log;

    private ToastUI ToastUI => field ??= _services.GetRequiredService<ToastUI>();
    private UICommander UICommander => field ??= _services.GetRequiredService<UICommander>();

    public WindowsLogAccessor(IServiceProvider services)
    {
        _services = services;
        _log = services.LogFor<WindowsLogAccessor>();
        if (!MauiDiagnostics.AppDataLogFilePath.IsEmpty)
            GetLogFile = OpenLogFileInternal;
    }

 #pragma warning disable CA1822
    public string ActionName => "Open log file";
 #pragma warning restore CA1822

    public Func<Task>? GetLogFile { get; }

    private Task OpenLogFileInternal()
    {
        try {
            var started = new Process {
                StartInfo = new ProcessStartInfo(MauiDiagnostics.AppDataLogFilePath) {
                    UseShellExecute = true,
                },
            }.Start();
            if (started) {
                ToastUI.Show("Got log file successfully.", "icon-checkmark-circle-2", ToastDismissDelay.Short);
                return Task.CompletedTask;
            }
        }
        catch (Exception e) {
            _log.LogWarning(e, "Failed to open log file: {FilePath}", MauiDiagnostics.AppDataLogFilePath);
        }
        UICommander.ShowError(StandardError.Constraint("Failed to get log file."));
        return Task.CompletedTask;
    }
}

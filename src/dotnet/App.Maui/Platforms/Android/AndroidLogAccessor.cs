using ActualChat.Chat.UI.Blazor.Services;
using Environment = Android.OS.Environment;

namespace ActualChat.App.Maui;

public class AndroidLogAccessor : IMauiLogAccessor
{
    private readonly ILogger _log;
    private readonly string _downloadFolder = "";

    public AndroidLogAccessor(ILogger<AndroidLogAccessor> log)
    {
        _log = log;
        var downloadFolder = Environment.GetExternalStoragePublicDirectory(Environment.DirectoryDownloads);
        if (downloadFolder == null) {
            log.LogWarning("Cannot dump logs. Reason: can not get download folder");
            return;
        }
        _downloadFolder = downloadFolder.AbsolutePath;
        GetLogFile = AccessLogFile;
    }

 #pragma warning disable CA1822
    public string ActionName => "Dump log file";
#pragma warning restore CA1822

    public Func<Task<bool>>? GetLogFile { get; }

    private Task<bool> AccessLogFile()
        => BackgroundTask.Run(AccessLogFileInternal, _log, "Access android log file failed");

    private async Task<bool> AccessLogFileInternal()
    {
        var now = DateTime.Now;
        var fileName = "log_"
            + (MauiSettings.IsDevApp ? "dev_actual_chat_" : "actual_chat_")
            + now.ToString("yyyyMMdd_HH.mm.ss")
            + ".txt";
        var filePath = Path.Combine(_downloadFolder, fileName);
        var age = TimeSpan.FromMinutes(30); // Get log for the last 30 minutes.
        var logStartThreshold = now.Add(age.Negate());
        var dumped = await DumpLogToFile(filePath, logStartThreshold).ConfigureAwait(false);
        // TODO(DF): add notification which opens log file
        return dumped;
    }

    private async Task<bool> DumpLogToFile(string filePath, DateTime logStartThreshold)
    {
        const string logcatCmd = "logcat";
        Process? process = null;
        string cmdArgs = "";
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(10));
        try {
            var pid = System.Environment.ProcessId;
            var tagFilter = MauiSettings.IsDevApp ? "dev\\.actual\\.chat" : "actual\\.chat";
            var regexFilter = $"{pid}|{tagFilter}";

            cmdArgs = "-v threadtime -d"
                + $" --regex=\"{regexFilter}\""
                + $" -T \"{logStartThreshold:MM-dd HH:mm:ss}.0\""
                + $" -f \"{filePath}\"";

            process = new Process {
                StartInfo = new ProcessStartInfo(logcatCmd) {
                    Arguments = cmdArgs,
                },
            };
            process.Start();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) {
            _log.LogWarning(e, "Failed to dump logs to file '{FilePath}'. Executing command: '{Command} {Arguments}'",
                Path.GetFileName(filePath), logcatCmd, cmdArgs);
        }
        finally {
            if (process is { HasExited: false })
                try {
                    process.Kill(true);
                }
                catch (Exception e2) {
                    _log.LogWarning(e2, "Failed to kill process. Executing command: '{Command} {Arguments}'",
                        logcatCmd, cmdArgs);
                }
            process?.Close();
        }
        return false;
    }
}

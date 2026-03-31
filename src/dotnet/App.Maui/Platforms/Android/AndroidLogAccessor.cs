using ActualChat.UI.Blazor;
using Android.App;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualLab.Fusion.UI;
using Application = Android.App.Application;
using Environment = Android.OS.Environment;

namespace ActualChat.App.Maui;

public class AndroidLogAccessor : IMauiLogAccessor
{
    private const string LogcatCmd = "logcat";
    private readonly IServiceProvider _services;
    private readonly ILogger _log;
    private readonly string _downloadFolder = "";

    private ToastUI ToastUI => field ??= _services.GetRequiredService<ToastUI>();
    private UICommander UICommander => field ??= _services.GetRequiredService<UICommander>();

    public AndroidLogAccessor(IServiceProvider services)
    {
        _services = services;
        _log = services.LogFor<AndroidLogAccessor>();
        var downloadFolder = Environment.GetExternalStoragePublicDirectory(Environment.DirectoryDownloads);
        if (downloadFolder == null) {
            _log.LogWarning("Cannot dump logs. Reason: can not get download folder");
            return;
        }
        _downloadFolder = downloadFolder.AbsolutePath;
        GetLogFile = AccessLogFile;
    }

 #pragma warning disable CA1822
    public string ActionName => "Dump log file";
#pragma warning restore CA1822

    public Func<Task>? GetLogFile { get; }

    private Task AccessLogFile()
        => BackgroundTask.Run(AccessLogFileInternal, _log, "Access android log file failed");

    private async Task AccessLogFileInternal()
    {
        try {
            var now = DateTime.Now;
            var fileName = "log_"
                + (MauiSettings.IsDevApp ? "dev_actual_chat_" : "actual_chat_")
                + now.ToString("yyyyMMdd_HH.mm.ss")
                + ".txt";
            var filePath = Path.Combine(_downloadFolder, fileName);
            var age = TimeSpan.FromMinutes(30); // Get log for the last 30 minutes.
            var logStartThreshold = now.Add(age.Negate());
            var pids = await ExtractPIDs(logStartThreshold).ConfigureAwait(false);
            if (pids.Count == 0)
                _log.LogWarning("No PIDs found in the log");
            var pid = System.Environment.ProcessId;
            if (!pids.Contains(pid))
                pids.Add(pid);
            await DumpLogToFile(filePath, pids, logStartThreshold).ConfigureAwait(false);
            ShowLogSavedNotification(filePath);
            ToastUI.Show("Log file saved to the Download folder.", "icon-checkmark-circle-2", ToastDismissDelay.Short);
        }
        catch (Exception e) {
            _log.LogWarning(e, "Failed to dump log file");
            UICommander.ShowError(StandardError.Constraint("Failed to get log file."));
        }
    }

    private void ShowLogSavedNotification(string filePath)
    {
        try {
            var context = Application.Context;
            var downloadManager = (DownloadManager)context.GetSystemService(Android.Content.Context.DownloadService)!;
            var file = new Java.IO.File(filePath);
            var fileInfo = new FileInfo(filePath);
#pragma warning disable CA1422 // Deprecated but still functional; no direct replacement available
            downloadManager.AddCompletedDownload(
                file.Name,
                "Lod dump saved to Downloads",
                true,
                "text/plain",
                filePath,
                fileInfo.Length,
                true);
#pragma warning restore CA1422
        }
        catch (Exception e) {
            _log.LogWarning(e, "Failed to show log saved notification");
        }
    }

    private async Task<ICollection<int>> ExtractPIDs(DateTime logStartThreshold)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(10));
        var tagFilter = MauiDiagnostics.LogTag;
        var cmdArgs = "-v process -d"
            + $" -T \"{logStartThreshold:MM-dd HH:mm:ss}.0\""
            + $" {tagFilter}:I *:S";

        var result = new HashSet<int>();
        try {
            await ExecuteLogcat(
                    cmdArgs,
                    line => {
                        // Log message format example:
                        // I(17896) (0001-17896) [@trace] MauiApp.MauiProgram
                        var index = line.IndexOf(')');
                        if (index >= 0) {
                            const int sPidStartIndex = 2;
                            var sPid = line.Substring(sPidStartIndex, index - sPidStartIndex);
                            if (int.TryParse(sPid, out var pid))
                                result.Add(pid);
                        }
                        return Task.CompletedTask;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            _log.LogWarning(e,
                "Failed to get process ids'. Executing command: '{Command} {Arguments}'",
                LogcatCmd,
                cmdArgs);
        }
        return result;
    }

    private async Task DumpLogToFile(string filePath, ICollection<int> pids, DateTime logStartThreshold)
    {
        var outputFile = new StreamWriter(filePath);
        await using var _ = outputFile.ConfigureAwait(false);
        await DumpCrashBufferLogToFile(outputFile).ConfigureAwait(false);
        await DumpMainBufferLogToFile(outputFile, pids, logStartThreshold).ConfigureAwait(false);
    }

    private Task<bool> DumpCrashBufferLogToFile(StreamWriter outputFile)
    {
        var cmdArgs = "-v threadtime -d -b crash";

        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(10));

        return DumpLogToFile(outputFile, cmdArgs, null, cancellationToken);
    }

    private Task<bool> DumpMainBufferLogToFile(StreamWriter outputFile, ICollection<int> pids, DateTime logStartThreshold)
    {
        var pidFilters = pids.Select(c => c.ToString()).ToArray();
        var tagFilter = MauiDiagnostics.LogTag;
        const string bufferFilter = "--------- beginning of";

        var cmdArgs = "-v threadtime -d"
            + $" -T \"{logStartThreshold:MM-dd HH:mm:ss}.0\" -b main";

        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(10));

        return DumpLogToFile(outputFile, cmdArgs, LineFilter, cancellationToken);

        bool LineFilter(string line)
        {
            var match = line.Contains(tagFilter)
                || line.StartsWith(bufferFilter);
            if (!match) {
                foreach (var pidFilter in pidFilters) {
                    if (line.Contains(pidFilter)) {
                        match = true;
                        break;
                    }
                }
            }
            if (!match)
                return false;
            // NOTE(DF): some logs (e.g. from Andrey Y.) contain a lot of these noisy lines
            // that are not helpful for us but increase log file size.
            if (line.Contains("setRequestedFrameRate frameRate="))
                return false;
            return match;
        }
    }

    private async Task<bool> DumpLogToFile(StreamWriter outputFile, string cmdArgs, Func<string, bool>? lineFilter, CancellationToken cancellationToken)
        => await ExecuteLogcat(
                cmdArgs,
                async line => {
                    if (lineFilter?.Invoke(line) ?? true)
                        await outputFile.WriteLineAsync(line).ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<bool> ExecuteLogcat(
        string cmdArgs,
        Func<string, Task> lineHandler,
        CancellationToken cancellationToken)
    {
        Process? process = null;
        try {
            process = new Process {
                StartInfo = new ProcessStartInfo(LogcatCmd) {
                    Arguments = cmdArgs,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
            };
            process.Start();
            using var processOutput = process.StandardOutput;
            while (true) {
                string? line = await processOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                    break;
                if (line.IsNullOrEmpty())
                    continue;
                await lineHandler(line).ConfigureAwait(false);
            }
            return true;
        }
        catch (Exception e) {
            _log.LogWarning(e,
                "Failed to dump logs to file. Executing command: '{Command} {Arguments}'",
                LogcatCmd,
                cmdArgs);
            throw;
        }
        finally {
            if (process is { HasExited: false })
                try {
                    process.Kill(true);
                }
                catch (Exception e2) {
                    _log.LogWarning(e2, "Failed to kill process. Executing command: '{Command} {Arguments}'",
                        LogcatCmd, cmdArgs);
                }
            process?.Close();
        }
    }
}

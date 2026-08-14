using UIKit;

namespace ActualChat.App.Maui.Activities;

/// <summary>
/// Wraps <see cref="UIApplication.BeginBackgroundTask(Action)"/> so uploads can
/// continue briefly after the app is backgrounded. iOS grants ~30 seconds
/// guaranteed, commonly extended to ~3 minutes on modern OS versions. Once the
/// OS calls the expiration handler we must end the task; further progress is
/// deferred until the app is foreground again, and the TUS upload client
/// resumes from the last persisted offset on next launch.
/// </summary>
public sealed class IosUploadKeepAlive
{
    private readonly ILogger<IosUploadKeepAlive> _log;
    private readonly object _lock = new();
    private nint _taskId = UIApplication.BackgroundTaskInvalid;

    public IosUploadKeepAlive(ILogger<IosUploadKeepAlive> log)
        => _log = log;

    public void Begin(string reason)
    {
        lock (_lock) {
            if (_taskId != UIApplication.BackgroundTaskInvalid)
                return;
            _taskId = UIApplication.SharedApplication.BeginBackgroundTask(reason, OnExpired);
            _log.LogInformation("BeginBackgroundTask '{Reason}' -> id={TaskId}", reason, (long)_taskId);
        }
    }

    public void End()
    {
        nint id;
        lock (_lock) {
            if (_taskId == UIApplication.BackgroundTaskInvalid)
                return;
            id = _taskId;
            _taskId = UIApplication.BackgroundTaskInvalid;
        }
        _log.LogInformation("EndBackgroundTask id={TaskId}", (long)id);
        UIApplication.SharedApplication.EndBackgroundTask(id);
    }

    private void OnExpired()
    {
        _log.LogWarning("iOS BeginBackgroundTask expired before work completed");
        End();
    }
}

namespace ActualChat.UI.Blazor.App.Services;

public class UploadApp(AppUIHub hub)
{
    private UploadSessions UploadSessions => hub.UploadSessions;
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= hub.LogFor<UploadApp>();

    public Task Start()
        => BackgroundTask.Run(StartInternal);

    private async Task StartInternal()
    {
        var services = hub.Services;
        try {
            var repo = services.GetRequiredService<IUploadSessionRepository>();
            var deleteTasks = new List<Task>();
            var checkTasks = new List<Task>();
            var sessions = await UploadSessions.GetActiveSessionsAsync().ConfigureAwait(false);

            foreach (var session1 in sessions) {
                var session = await UploadSessions.GetSession(session1.SessionId).ConfigureAwait(false);
                session.FileProvider.Initialize(services);
                if (session1.Status is UploadStatus.Completed or UploadStatus.Cancelled) {
                    var deleteTask = UploadSessions.DeleteSession(session1.SessionId);
                    deleteTasks.Add(deleteTask);
                    continue;
                }

                var checkTask = ResumeInternal(session ,deleteTasks);
                checkTasks.Add(checkTask);
            }

            await Task.WhenAll(checkTasks).ConfigureAwait(false);
            foreach (var checkTask in checkTasks) {
                try {
                    await checkTask.ConfigureAwait(false);
                }
                catch (Exception ex) {
                    Log.LogWarning(ex, "Failed to resume session");
                }
            }
            await Task.WhenAll(deleteTasks).ConfigureAwait(false);
            foreach (var deleteTask in deleteTasks) {
                try {
                    await deleteTask.ConfigureAwait(false);
                }
                catch (Exception ex) {
                    Log.LogWarning(ex, "Failed to delete session");
                }
            }
            await repo.Flush().ConfigureAwait(false);
        }
        catch(Exception ex2) {
            Log.LogError(ex2, "Failed to resume upload sessions");
        }

        // Resume sending messages
        _ = services.GetRequiredService<SendingMessages>();
    }

    private async Task<bool> ResumeInternal(UploadSession session, List<Task> deleteTasks)
    {
        if (!await CheckAccessSafely(session).ConfigureAwait(false)) {
            deleteTasks.Add(BackgroundTask.Run(async () => {
                Log.LogInformation("About to cancel session {SessionId} because file is not accessible", session.SessionId);
                await UploadSessions.CancelSession(session.SessionId).ConfigureAwait(false);
                await UploadSessions.DeleteSession(session.SessionId).ConfigureAwait(false);
            }));
            return false;
        }

        await UploadSessions.ResumeSession(session.SessionId).ConfigureAwait(false);
        Log.LogInformation("Resumed session {SessionId}", session.SessionId);
        return true;
    }

    private async Task<bool> CheckAccessSafely(UploadSession session)
    {
        try {
            var checkAccess = await session.FileProvider.CheckAccess().ConfigureAwait(false);
            if (checkAccess)
                return true;
        }
        catch (Exception ex) {
            Log.LogWarning(ex, "Failed to check access to file");
        }
        return false;
    }
}

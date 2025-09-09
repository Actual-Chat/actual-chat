namespace ActualChat.UI.Blazor.App.Services;

public class UploadApp(UploadSessionManager uploadSessionManager, IServiceProvider services)
{
    private ILogger Log => services.LogFor(GetType());

    public Task Start()
        => BackgroundTask.Run(StartInternal);

    private async Task StartInternal()
    {
        try {
            var repo = services.GetRequiredService<IUploadSessionRepository>();
            var deleteTasks = new List<Task>();
            var checkTasks = new List<Task>();
            var sessions = await uploadSessionManager.GetActiveSessionsAsync().ConfigureAwait(false);

            foreach (var session1 in sessions) {
                var session = await uploadSessionManager.GetSession(session1.SessionId).ConfigureAwait(false);
                session.FileProvider.Initialize(services);
                if (session1.Status is UploadStatus.Completed or UploadStatus.Cancelled) {
                    var deleteTask = uploadSessionManager.DeleteSession(session1.SessionId);
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
    }

    private async Task<bool> ResumeInternal(UploadSession session, List<Task> deleteTasks)
    {
        if (!await CheckAccessSafely(session).ConfigureAwait(false)) {
            deleteTasks.Add(BackgroundTask.Run(async () => {
                Log.LogInformation("About to cancel session {SessionId} because file is not accessible", session.SessionId);
                await uploadSessionManager.CancelSession(session.SessionId).ConfigureAwait(false);
                await uploadSessionManager.DeleteSession(session.SessionId).ConfigureAwait(false);
            }));
            return false;
        }

        await uploadSessionManager.ResumeSession(session.SessionId).ConfigureAwait(false);
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

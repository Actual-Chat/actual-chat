namespace ActualChat.UI.Blazor.App.Services;

public class UploadApp(UploadSessionManager uploadSessionManager, IServiceProvider services)
{
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
                if (session1.Status is UploadStatus.Completed or UploadStatus.Cancelled) {
                    var deleteTask = uploadSessionManager.DeleteSession(session1.SessionId);
                    deleteTasks.Add(deleteTask);
                    continue;
                }

                var context = new UploadSessionContext(session, services);
                var checkTask = ResumeInternal(session, context);
                checkTasks.Add(checkTask);
            }

            await Task.WhenAll(checkTasks).ConfigureAwait(false);
            foreach (var checkTask in checkTasks) {
                try {
                    await checkTask.ConfigureAwait(false);
                }
                catch (Exception e) {
                    //Tracer.Point($"Failed to resume session: {e}");
                }
            }
            await Task.WhenAll(deleteTasks).ConfigureAwait(false);
            foreach (var deleteTask in deleteTasks) {
                try {
                    await deleteTask.ConfigureAwait(false);
                }
                catch (Exception e) {
                    //Tracer.Point($"Failed to resume session: {e}");
                }
            }
            await repo.Flush().ConfigureAwait(false);
        }
        catch(Exception ex2) {
            // Intended
        }
    }

    private async Task ResumeInternal(UploadSession session, UploadSessionContext context)
    {
        try {
            var checkAccess = await session.FileProvider.CheckAccess(context).ConfigureAwait(false);
            if (!checkAccess)
                return;
        }
        catch (Exception ex) {
            return;
        }

        await uploadSessionManager.ResumeSession(session.SessionId).ConfigureAwait(false);
    }
}

public record UploadSessionContext(UploadSession Session, IServiceProvider Services);

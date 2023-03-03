namespace ActualChat.App.Maui.Services;

public record ClientAppSettings
{
    private readonly TaskSource<string> _taskSource = TaskSource.New<string>(true);

    public string SessionId {
        get {
            var task = GetSessionId();
            if (!task.IsCompleted)
                throw StandardError.Internal("Invalid prop usage. Task is not completed yet.");
 #pragma warning disable VSTHRD002
            return task.GetAwaiter().GetResult();
 #pragma warning restore VSTHRD002
        }
    }

    public Task<string> GetSessionId() => _taskSource.Task;

    public void SetSessionId(string sessionId)
        => _taskSource.SetResult(sessionId);
}

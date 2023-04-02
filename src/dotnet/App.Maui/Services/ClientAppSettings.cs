namespace ActualChat.App.Maui.Services;

public record ClientAppSettings
{
    private readonly Task<string> _sessionIdTask = TaskSource.New<string>(true).Task;

    public string SessionId {
        get {
            var task = GetSessionId();
            if (!task.IsCompleted)
                throw StandardError.Internal("SetSessionId is not invoked yet.");

 #pragma warning disable VSTHRD002
            return task.GetAwaiter().GetResult();
 #pragma warning restore VSTHRD002
        }
    }

    public Task<string> GetSessionId()
        => _sessionIdTask;

    public void SetSessionId(string sessionId)
        => TaskSource.For(_sessionIdTask).SetResult(sessionId);
}

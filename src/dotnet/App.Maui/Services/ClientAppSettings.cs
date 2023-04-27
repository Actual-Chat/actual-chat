namespace ActualChat.App.Maui.Services;

public record ClientAppSettings
{
    private readonly TaskCompletionSource<Session> _sessionSource = TaskCompletionSourceExt.New<Session>();

    public Uri BaseUri { get; }
    public string BaseUrl { get; }
    public Task<Session> WhenSessionReady => _sessionSource.Task;

    public Session Session {
        get {
            var sessionIdTask = _sessionSource.Task;
            if (!sessionIdTask.IsCompleted)
                throw StandardError.Internal("SessionId wasn't set yet.");

 #pragma warning disable VSTHRD002
            return sessionIdTask.Result;
 #pragma warning restore VSTHRD002
        }
        set {
            if (!_sessionSource.TrySetResult(value))
                throw StandardError.Internal("SessionId is already set.");
        }
    }

    public ClientAppSettings(string baseUrl)
    {
        baseUrl = baseUrl.EnsureSuffix("/");
        BaseUrl = baseUrl;
        BaseUri = baseUrl.ToUri();
    }
}

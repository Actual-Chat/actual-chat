namespace ActualChat.App.Maui.Services;

public record ClientAppSettings
{
    private readonly TaskCompletionSource<string> _sessionIdSource = TaskCompletionSourceExt.New<string>();

    public Uri BaseUri { get; }
    public string BaseUrl { get; }

    public string SessionId {
        get {
            var sessionIdTask = _sessionIdSource.Task;
            if (!sessionIdTask.IsCompleted)
                throw StandardError.Internal("SessionId wasn't set yet.");

 #pragma warning disable VSTHRD002
            return sessionIdTask.GetAwaiter().GetResult();
 #pragma warning restore VSTHRD002
        }
        set {
            if (!_sessionIdSource.TrySetResult(value))
                throw StandardError.Internal("SessionId is already set.");
        }
    }

    public ClientAppSettings(string baseUrl)
    {
        baseUrl = baseUrl.EnsureSuffix("/");
        BaseUrl = baseUrl;
        BaseUri = baseUrl.ToUri();
    }

    public Task<string> GetSessionId(CancellationToken cancellationToken = default)
        => _sessionIdSource.Task.WaitAsync(cancellationToken);
}

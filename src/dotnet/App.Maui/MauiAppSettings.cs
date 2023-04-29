namespace ActualChat.App.Maui;

public sealed record MauiAppSettings
{
    private readonly TaskCompletionSource<Session> _sessionSource = TaskCompletionSourceExt.New<Session>();

    public Uri BaseUri { get; }
    public string BaseUrl { get; }
    public Task<Session> WhenSessionReady => _sessionSource.Task;

    public Session Session {
        get {
            var sessionTask = _sessionSource.Task;
            if (!sessionTask.IsCompleted)
                throw StandardError.Internal($"{nameof(Session)} wasn't set yet.");

 #pragma warning disable VSTHRD002
            return sessionTask.Result;
 #pragma warning restore VSTHRD002
        }
        set {
            if (!_sessionSource.TrySetResult(value))
                throw StandardError.Internal($"{nameof(Session)} is already set.");
        }
    }

    public MauiAppSettings(string baseUrl)
    {
        baseUrl = baseUrl.EnsureSuffix("/");
        BaseUrl = baseUrl;
        BaseUri = baseUrl.ToUri();
    }
}

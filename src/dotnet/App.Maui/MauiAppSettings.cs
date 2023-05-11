namespace ActualChat.App.Maui;

public sealed record MauiAppSettings
{
    private readonly TaskCompletionSource<Session> _sessionSource = TaskCompletionSourceExt.New<Session>();

    public Uri BaseUri { get; }
    public string BaseUrl { get; }
    public Task<Session> SessionTask => _sessionSource.Task;

    public void SetupSession(Session session)
    {
        if (!_sessionSource.TrySetResult(session))
            throw StandardError.Internal($"{nameof(Session)} is already set.");
    }

    public MauiAppSettings(string baseUrl)
    {
        baseUrl = baseUrl.EnsureSuffix("/");
        BaseUrl = baseUrl;
        BaseUri = baseUrl.ToUri();
    }
}

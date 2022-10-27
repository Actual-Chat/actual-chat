namespace ActualChat.App.Maui.Services;

public partial class NativeHttpClientFactory : IHttpClientFactory
{
    private readonly ConcurrentDictionary<string, HttpClient> _clients = new ();

    public HttpClient CreateClient(string name)
        => _clients.GetOrAdd(name, _ =>
            CreatePlatformMessageHandler() is { } handler
                ? new HttpClient(handler)
                : new HttpClient());

    private partial HttpMessageHandler? CreatePlatformMessageHandler();
}

namespace ActualChat.Media.IntegrationTests;

public class HttpClientFactoryMock : IHttpClientFactory
{
    private readonly Dictionary<string, HttpClient> _mocks = new (StringComparer.Ordinal);

    public void Set(HttpClient httpClient)
        => Set("", httpClient);

    public void Set(string name, HttpClient httpClient)
        => _mocks[name] = httpClient;

    public HttpClient CreateClient(string name)
        => _mocks.GetValueOrDefault(name) ?? _mocks[""];
}

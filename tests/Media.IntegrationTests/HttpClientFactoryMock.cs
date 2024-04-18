namespace ActualChat.Media.IntegrationTests;

public class HttpClientFactoryMock(HttpHandlerMock handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
        => new (handler);
}

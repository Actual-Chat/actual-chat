namespace ActualChat.Media.IntegrationTests;

public class HttpHandlerMock : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responseFactories
        = new (StringComparer.OrdinalIgnoreCase);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => _responseFactories[request.RequestUri?.AbsoluteUri](request, cancellationToken);

    public HttpHandlerMock Setup(string url, Func<HttpRequestMessage, HttpResponseMessage> factory)
        => Setup(url, (request, _) => Task.FromResult(factory(request)));

    public HttpHandlerMock Setup(string url, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> factory)
    {
        _responseFactories[url] = factory;
        return this;
    }
}

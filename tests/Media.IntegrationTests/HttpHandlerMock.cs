namespace ActualChat.Media.IntegrationTests;

public class HttpHandlerMock : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responseFactories
        = new (StringComparer.OrdinalIgnoreCase);
    private Action<HttpRequestMessage>? _onRequest;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _onRequest?.Invoke(request);
        return _responseFactories[request.RequestUri?.AbsoluteUri](request, cancellationToken);
    }

    public HttpHandlerMock Setup(string url, Func<HttpRequestMessage, HttpResponseMessage> factory)
        => Setup(url, (request, _) => Task.FromResult(factory(request)));

    public HttpHandlerMock Setup(string url, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> factory)
    {
        _responseFactories[url] = factory;
        return this;
    }

    // The mock is one shared instance per fixture, so the observer is replaced, not accumulated
    public HttpHandlerMock OnRequest(Action<HttpRequestMessage>? onRequest)
    {
        _onRequest = onRequest;
        return this;
    }
}

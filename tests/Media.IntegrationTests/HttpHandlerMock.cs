namespace ActualChat.Media.IntegrationTests;

public class HttpHandlerMock : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _responseFactories = new (StringComparer.OrdinalIgnoreCase);
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => Task.FromResult(_responseFactories[request.RequestUri?.AbsoluteUri](request));

    public HttpHandlerMock Setup(string url, Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        _responseFactories[url] = factory;
        return this;
    }
}

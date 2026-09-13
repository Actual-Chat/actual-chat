namespace ActualChat.ContentCaching;

public sealed class HttpContentHandler(HttpClient client) : IContentHandler
{
    public async ValueTask<HttpResponseMessage?> Handle(
        ContentRequest request,
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(request.Method, request.Url);
        foreach (var header in request.Headers)
            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
                throw new ArgumentException("Unsupported content request header.", nameof(request));

        return await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }
}

using ActualChat.Hashing;

namespace ActualChat.ContentCaching;

public sealed class LoggingContentHandler(IContentHandler? downstream = null, ILogger? log = null) : IContentHandler
{
    public ValueTask<HttpResponseMessage?> Handle(ContentRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (log?.IsEnabled(LogLevel.Debug) == true)
            log.LogDebug("Content request: {Method} {Scheme}://{Host}, key {Key}",
                request.Method, request.Url.Scheme, request.Url.Host,
                request.Url.AbsoluteUri.Hash().SHA256().AlphaNumeric());
        return downstream?.Handle(request, cancellationToken) ?? ValueTask.FromResult<HttpResponseMessage?>(null);
    }
}

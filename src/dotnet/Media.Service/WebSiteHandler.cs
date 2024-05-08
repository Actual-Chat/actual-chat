using ActualChat.Media.Module;

namespace ActualChat.Media;

public sealed class WebSiteHandler(MediaSettings settings, ImageGrabber imageGrabber, ILogger<WebSiteHandler> Log) : ICrawlingHandler
{
    public bool Supports(HttpResponseMessage response)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        return OrdinalIgnoreCaseEquals(contentType, "text/html");
    }

    public async Task<CrawledLink> Handle(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var requestUri = response.RequestMessage?.RequestUri;
        var graph = await ParseOpenGraph(response.Content, requestUri, cancellationToken).ConfigureAwait(false);
        if (graph is null)
            return CrawledLink.None;

        var mediaId = MediaId.None;
        try {
            mediaId = await imageGrabber.GrabImage(graph.ImageUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to grab image with url '{ImageUrl}' for page with url '{PageUrl}'",
                graph.ImageUrl, requestUri);
        }
        return new (mediaId, graph);
    }

    private async Task<OpenGraph?> ParseOpenGraph(
        HttpContent content,
        Uri? requestUri,
        CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(settings.GraphParseTimeout);
        var html = await content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        return OpenGraphParser.Parse(html, requestUri);
    }
}

using ActualChat.Media.Module;

namespace ActualChat.Media;

public sealed class WebSiteHandler(MediaSettings settings, ImageGrabber imageGrabber) : ICrawlingHandler
{
    public bool Supports(HttpResponseMessage response)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        return OrdinalIgnoreCaseEquals(contentType, "text/html");
    }

    public async Task<CrawledLink> Handle(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var graph = await ParseOpenGraph(response.Content, cancellationToken).ConfigureAwait(false);
        if (graph is null)
            return CrawledLink.None;

        var mediaId = await imageGrabber.GrabImage(graph.ImageUrl, cancellationToken).ConfigureAwait(false);
        return new (mediaId, graph);
    }

    private async Task<OpenGraph?> ParseOpenGraph(HttpContent content, CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(settings.GraphParseTimeout);
        var html = await content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        return OpenGraphParser.Parse(html);
    }
}

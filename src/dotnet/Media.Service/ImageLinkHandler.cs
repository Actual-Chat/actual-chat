namespace ActualChat.Media;

public sealed class ImageLinkHandler(ImageGrabber imageGrabber, ILogger<ImageLinkHandler> log) : ICrawlingHandler
{
    public bool Supports(HttpResponseMessage response)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        return contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CrawledLink> Handle(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var mediaId = (MediaId?)null;
        var retryDelay = (TimeSpan?)null;
        try {
            mediaId = await imageGrabber.GetOrGrab(response.RequestMessage!.RequestUri!.AbsoluteUri, cancellationToken).ConfigureAwait(false);
        }
        catch (RateLimitExceededException e) {
            retryDelay = e.RetryDelay;
            log.LogWarning("Rate-limited grabbing image with url '{ImageUrl}': {Message}",
                response.RequestMessage?.RequestUri, e.Message);
        }
        catch (Exception e) {
            log.LogWarning(e, "Failed to grab image with url '{ImageUrl}'",
                response.RequestMessage?.RequestUri);
        }
        return new CrawledLink(mediaId, OpenGraph.None, retryDelay);
    }
}

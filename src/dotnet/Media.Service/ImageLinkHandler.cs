namespace ActualChat.Media;

public sealed class ImageLinkHandler(ImageGrabber imageGrabber, ILogger<ImageLinkHandler> log) : ICrawlingHandler
{
    public bool Supports(HttpResponseMessage response)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        return contentType.OrdinalIgnoreCaseStartsWith("image/");
    }

    public async Task<CrawledLink> Handle(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var mediaId = MediaId.None;
        try {
            mediaId = await imageGrabber.GetOrGrab(response.RequestMessage!.RequestUri!.AbsoluteUri, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            log.LogWarning(e, "Failed to grab image with url '{ImageUrl}'",
                response.RequestMessage?.RequestUri);
        }
        return new CrawledLink(mediaId, OpenGraph.None);
    }
}

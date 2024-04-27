namespace ActualChat.Media;

public sealed class ImageLinkHandler(ImageGrabber imageGrabber) : ICrawlingHandler
{
    public bool Supports(HttpResponseMessage response)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        return contentType.OrdinalIgnoreCaseStartsWith("image/");
    }

    public async Task<CrawledLink> Handle(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var mediaId = await imageGrabber.GrabImage(response, cancellationToken).ConfigureAwait(false);
        return new CrawledLink(mediaId, OpenGraph.None);
    }
}

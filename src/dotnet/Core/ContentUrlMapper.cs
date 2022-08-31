namespace ActualChat;

public class ContentUrlMapper
{
    private readonly bool _transformUri;
    private readonly string _contentBaseUri;
    private readonly string _mediaBaseUri;

    public ContentUrlMapper(UriMapper uriMapper)
    {
        var baseUri = uriMapper.BaseUri;

        _transformUri = baseUri.Host.OrdinalIgnoreCaseEndsWith("actual.chat");
        _contentBaseUri = _transformUri ? $"{baseUri.Scheme}://cdn.{baseUri.Host}/" : "";
        _mediaBaseUri = _transformUri ? $"{baseUri.Scheme}://media.{baseUri.Host}/" : "";

        // TODO: remove this workaround when new cert is available
        if (OrdinalIgnoreCaseEquals(baseUri.Host, "dev.actual.chat")) {
            _contentBaseUri = $"{baseUri.Scheme}://cdn-{baseUri.Host}/";
            _mediaBaseUri = $"{baseUri.Scheme}://media-{baseUri.Host}/";
        }
    }

    public string ContentUrl(string contentId)
    {
        if (Uri.TryCreate(contentId, UriKind.Absolute, out _))
            return contentId;
        if (_transformUri)
            return _contentBaseUri + contentId;
        return "/api/content/" + contentId;
    }

    public string ImagePreviewUrl(string imageUrl, int maxWidth = 800, int maxHeight = 600)
    {
        if (_transformUri)
            return _mediaBaseUri + $"{maxWidth}x{maxHeight},fit/" + imageUrl;
        return imageUrl;
    }

    public string PicturePreviewUrl(string imageUrl)
        => _transformUri ? $"{_mediaBaseUri}100/{imageUrl}" : imageUrl;
}

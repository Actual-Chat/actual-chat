namespace ActualChat.UI.Blazor.Services;

public class ContentUrlMapper
{
    private readonly bool _transformUri;
    private readonly string _contentBaseUri;
    private readonly string _mediaBaseUri;

    public ContentUrlMapper(UriMapper uriMapper)
    {
        var baseUri = uriMapper.BaseUri;

        _transformUri = baseUri.Host.EndsWith("actual.chat", StringComparison.OrdinalIgnoreCase);
        _contentBaseUri = _transformUri ? $"{baseUri.Scheme}://cdn.{baseUri.Host}/" : "";
        _mediaBaseUri = _transformUri ? $"{baseUri.Scheme}://media.{baseUri.Host}/" : "";

        // TODO: remove this workaround when new cert is available
        if (baseUri.Host.Equals("dev.actual.chat", StringComparison.OrdinalIgnoreCase)) {
            _contentBaseUri = $"{baseUri.Scheme}://cdn-{baseUri.Host}/";
            _mediaBaseUri = $"{baseUri.Scheme}://media-{baseUri.Host}/";
        }
    }

    public string ContentUrl(string contentId)
    {
        if (_transformUri)
            return _contentBaseUri + contentId;
        return "/api/content/" + contentId;
    }

    public string ImagePreviewUrl(string imageUrl)
    {
        if (_transformUri)
            return _mediaBaseUri + "400x300,fit/" + imageUrl;
        return imageUrl;
    }

    public string PicturePreviewUrl(string imageUrl)
        => _transformUri ? $"{_mediaBaseUri}100/{imageUrl}" : imageUrl;
}

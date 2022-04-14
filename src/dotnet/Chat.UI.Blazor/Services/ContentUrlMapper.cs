namespace ActualChat.UI.Blazor.Services;

public class ContentUrlMapper
{
    private readonly bool _transformUri;
    private readonly string _contentBaseUri;
    private readonly string _mediaBaseUri;

    public ContentUrlMapper(NavigationManager nav)
    {
        var baseUri = new Uri(nav.BaseUri);
        _transformUri = StringComparer.OrdinalIgnoreCase.Equals(baseUri.Host, "local.actual.chat");
        _contentBaseUri = _transformUri ? $"{baseUri.Scheme}://cdn.{baseUri.Host}/" : "";
        _mediaBaseUri = _transformUri ? $"{baseUri.Scheme}://media.{baseUri.Host}/" : "";
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
}

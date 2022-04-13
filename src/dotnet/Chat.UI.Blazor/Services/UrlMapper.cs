namespace ActualChat.UI.Blazor.Services;

public class UrlMapper
{
    private readonly NavigationManager _nav;
    private readonly Uri _baseUri;
    private readonly bool _isActualChatDomain;

    public UrlMapper(NavigationManager nav)
    {
        _nav = nav;
        _baseUri = new Uri(_nav.BaseUri);
        _isActualChatDomain = _baseUri.Host.EndsWith("actual.chat", StringComparison.OrdinalIgnoreCase);
    }

    public string ContentUrl(string contentId)
    {
        var mapEndpoint = _isActualChatDomain;
        if (mapEndpoint) {
            return $"{_baseUri.Scheme}://cdn.{_baseUri.Host}/" + contentId;
        }
        else {
            var url = "/api/content/" + contentId;
            return url;
        }
        // var url = "/api/content/" + contentId;
        // var absUrl = _nav.ToAbsoluteUri(url);
        // var correctedUrl = absUrl.ToString().Replace("localhost", "192.168.1.3", StringComparison.OrdinalIgnoreCase);
        // return correctedUrl;
    }

    public string ResizedImageUrl(string imageUrl)
    {
        var resizingSupported = _isActualChatDomain;
        if (resizingSupported) {
            return $"{_baseUri.Scheme}://media.{_baseUri.Host}/400,fit/" + imageUrl;
        }
        else {
            return imageUrl;
        }
    }
}

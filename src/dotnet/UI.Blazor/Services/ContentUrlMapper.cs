using ActualChat.Module;
using Microsoft.Extensions.Hosting;

namespace ActualChat.UI.Blazor.Services;

public class ContentUrlMapper
{
    private readonly bool _transformUri;
    private readonly string _contentBaseUri;
    private readonly string _mediaBaseUri;

    public ContentUrlMapper(NavigationManager nav, CoreSettings settings, IHostEnvironment env)
    {
        var baseUri = new Uri(nav.BaseUri);

        if (env.IsDevelopment()) {
            // TODO: refactor when new certificate with subdomain is available
            _transformUri = StringComparer.OrdinalIgnoreCase.Equals(baseUri.Host, "local.actual.chat");
            _contentBaseUri = _transformUri ? $"{baseUri.Scheme}://cdn.{baseUri.Host}/" : "";
            _mediaBaseUri = _transformUri ? $"{baseUri.Scheme}://media.{baseUri.Host}/" : "";
        }
        else {
            _transformUri = baseUri.Host.EndsWith("actual.chat", StringComparison.OrdinalIgnoreCase);
            // TODO: change to subdomain when new certificate is available
            _contentBaseUri = settings.UseCdnServer ? $"{baseUri.Scheme}://cdn-{baseUri.Host}/" : "";
            _mediaBaseUri = settings.UseMediaServer ? $"{baseUri.Scheme}://media-{baseUri.Host}/" : "";
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

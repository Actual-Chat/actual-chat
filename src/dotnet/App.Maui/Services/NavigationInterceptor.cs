using Microsoft.AspNetCore.Components;
namespace ActualChat.App.Maui.Services;

public class NavigationInterceptor
{
    private NavigationManager? Nav { get; set; }
    private UrlMapper UrlMapper { get; }

    public NavigationInterceptor(UrlMapper urlMapper)
        => UrlMapper = urlMapper;

    internal void Initialize(NavigationManager nav)
        => Nav = nav;

    internal bool TryIntercept(Uri uri)
    {
        if (Nav == null)
            return false;

        var baseUri = UrlMapper.BaseUri;
        if (baseUri.IsBaseOf(uri)) {
            var relativeUri = baseUri.MakeRelativeUri(uri);
            Nav.NavigateTo(relativeUri.ToString());
            return true;
        }
        return false;
    }
}

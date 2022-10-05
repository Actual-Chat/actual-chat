using Microsoft.AspNetCore.Components;

namespace ActualChat.App.Maui.Services;

public class NavigationInterceptor
{
    private UrlMapper UrlMapper { get; }

    public NavigationInterceptor(UrlMapper urlMapper)
        => UrlMapper = urlMapper;

    internal bool TryIntercept(Uri uri)
    {
        var nav = ScopedServiceLocator.IsInitialized
            ? ScopedServiceLocator.Services.GetRequiredService<NavigationManager>()
            : null;
        if (nav == null)
            return false;

        var baseUri = UrlMapper.BaseUri;
        if (baseUri.IsBaseOf(uri)) {
            var relativeUri = baseUri.MakeRelativeUri(uri);
            nav.NavigateTo(relativeUri.ToString());
            return true;
        }
        return false;
    }
}

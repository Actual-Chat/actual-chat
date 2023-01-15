using Microsoft.AspNetCore.Components;

namespace ActualChat.App.Maui.Services;

public class NavigationInterceptor
{
    private UrlMapper UrlMapper { get; }

    public NavigationInterceptor(IServiceProvider services)
        => UrlMapper = services.GetRequiredService<UrlMapper>();

    internal bool TryIntercept(Uri uri)
    {
        var nav = ScopedServicesAccessor.IsInitialized
            ? ScopedServicesAccessor.ScopedServices.GetRequiredService<NavigationManager>()
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

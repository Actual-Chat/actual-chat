using Microsoft.AspNetCore.Components;

namespace ActualChat.App.Maui.Services;

public class MauiNavigationInterceptor
{
    internal bool TryIntercept(Uri uri)
    {
        const string webViewAppHostAddress = "0.0.0.0";
        if (OrdinalEquals(uri.Host, webViewAppHostAddress))
            return false;
        if (!TryGetScopedServices(out var scopedServices))
            return false;

        var nav = scopedServices.GetRequiredService<NavigationManager>();
        var baseUri = AppSettings.BaseUri;
        if (!baseUri.IsBaseOf(uri))
            return false;

        var relativeUri = baseUri.MakeRelativeUri(uri);
        nav.NavigateTo(relativeUri.ToString());
        return true;
    }
}

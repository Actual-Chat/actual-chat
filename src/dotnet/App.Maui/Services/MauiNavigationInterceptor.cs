using ActualChat.UI.Blazor.Services;
using Microsoft.AspNetCore.Components.WebView;

namespace ActualChat.App.Maui.Services;

public class MauiNavigationInterceptor
{
    public void TryIntercept(Uri uri, UrlLoadingEventArgs eventArgs)
    {
        if (OrdinalEquals(uri.Host, MauiSettings.LocalHost)) {
            // Local MAUI app URL
            eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.OpenInWebView;
            return;
        }

        var baseUri = MauiSettings.BaseUri;
        if (!baseUri.IsBaseOf(uri)) {
            // Neither local MAUI app URL nor host URL
            eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.OpenExternally;
            return;
        }
        // If we're here, it's a host URL

        if (uri.PathAndQuery.OrdinalIgnoreCaseStartsWith("/mobileAuth")) {
            // It's a mobileAuth / mobileAuthV2 URL, we open them in WebView
            eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.OpenInWebView;
            return;
        }

        // It's a host URL, so we have to re-route it to the local one
        if (!TryGetScopedServices(out var scopedServices)) {
            // There is nothing we can do in this case
            eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.OpenExternally;
            return;
        }

        var history = scopedServices.GetRequiredService<History>();
        eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
        _ = history.Dispatcher.InvokeAsync(() => {
            var relativeUri = baseUri.MakeRelativeUri(uri);
            history.Nav.NavigateTo(relativeUri.ToString());
        });
    }
}

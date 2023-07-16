using ActualChat.UI.Blazor.Services;
using Microsoft.AspNetCore.Components.WebView;

namespace ActualChat.App.Maui.Services;

public class MauiNavigationInterceptor
{
    // ReSharper disable once CollectionNeverUpdated.Local
    private static HashSet<string> AllowedExternalHosts { get; } = MauiSettings.WebAuth.UseSystemBrowser
        ? new(StringComparer.Ordinal) { "accounts.google.com", "appleid.apple.com" }
        : new(StringComparer.Ordinal);

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
            eventArgs.UrlLoadingStrategy = AllowedExternalHosts.Contains(uri.Host)
                ? UrlLoadingStrategy.OpenInWebView
                : UrlLoadingStrategy.OpenExternally;
            return;
        }
        // If we're here, it's a host URL

        if (IsAllowedHostUri(uri)) {
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

    // Private methods

    private bool IsAllowedHostUri(Uri uri)
    {
        if (MauiSettings.WebAuth.UseSystemBrowser) {
            var pathAndQuery = uri.PathAndQuery.ToLowerInvariant();
            if (pathAndQuery.OrdinalStartsWith("/maui/"))
                return true;
            if (pathAndQuery.OrdinalStartsWith("/signin"))
                return true;
            if (pathAndQuery.OrdinalStartsWith("/signout"))
                return true;
        }
        return false;
    }
}

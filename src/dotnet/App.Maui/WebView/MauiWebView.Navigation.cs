using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Microsoft.AspNetCore.Components.WebView;

namespace ActualChat.App.Maui;

public partial class MauiWebView
{
    // ReSharper disable once CollectionNeverUpdated.Local
    private static readonly HashSet<string> AllowedExternalHosts = MauiSettings.WebAuth.UseSystemBrowser
        ? new(StringComparer.Ordinal) { "www.youtube.com" }
        : new(StringComparer.Ordinal) { "accounts.google.com", "appleid.apple.com" };

    public static readonly Uri BaseLocalUri = new($"https://{MauiSettings.LocalHost}/");
    public Uri LastUri { get; private set; } = BaseLocalUri;
    public Uri LastLocalUri { get; private set; } = BaseLocalUri;
    public bool IsOnLocalUri => LastUri == LastLocalUri;

    public async Task NavigateTo(string uri, bool hardReload = false)
    {
        if (!hardReload && ScopedServices is { } scopedServices) {
            // Soft navigation
            try {
                var hub = scopedServices.GetRequiredService<UIHub>();
                await hub.Dispatcher.InvokeSafeAsync(() => hub.Nav.NavigateTo(uri), Log).ConfigureAwait(false);
                return;
            }
            catch (Exception e) {
                Log.LogError(e, "Soft NavigateTo failed, retrying with hard navigation...");
            }
        }

        HardNavigateTo(uri);
    }

    // Private methods

    private void OnLoading(object? sender, UrlLoadingEventArgs eventArgs)
    {
        if (IsDead && Current == this) {
            MainThreadExt.InvokeLater(() => {
                if (Current == this)
                    MainPage.Current.RecreateWebView();
            });
            eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
            return;
        }
        if (LastResumeAt.Elapsed < TimeSpan.FromSeconds(0.5))
            MauiLivenessProbe.Check();

        var uri = eventArgs.Url;
        var isLocalUri = HandleLoading(uri, eventArgs);
        Tracer.Point($"{nameof(HandleLoading)}: Url: '{uri}' -> {eventArgs.UrlLoadingStrategy}, {(isLocalUri ? "local" : "external")}");
        if (eventArgs.UrlLoadingStrategy != UrlLoadingStrategy.OpenInWebView)
            return;

        LastUri = uri;
        if (isLocalUri)
            LastLocalUri = uri;
    }

    private bool HandleLoading(Uri uri, UrlLoadingEventArgs eventArgs)
    {
        var wasOnLocalUri = IsOnLocalUri;
        if (OrdinalEquals(uri.Host, MauiSettings.LocalHost)) {
            // Local MAUI app URL
            eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.OpenInWebView;
            return true;
        }

        if (!MauiSettings.BaseUri.IsBaseOf(uri)) {
            // Neither local MAUI app URL nor host URL
            var isAllowedExternalUri = AllowedExternalHosts.Contains(uri.Host);
            eventArgs.UrlLoadingStrategy = isAllowedExternalUri
                ? UrlLoadingStrategy.OpenInWebView
                : UrlLoadingStrategy.OpenExternally;
            return false;
        }

        // If we're here, it's a host URL

        if (IsAllowedHostUri(uri)) {
            // We never land here, coz IsAllowedHostUri(...) always returns false now
            if (uri.PathAndQuery.OrdinalIgnoreCaseStartsWith("/fusion/close")) {
                MainThreadExt.InvokeLater(() => HardNavigateTo(LastLocalUri.ToString()));
                eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
                return false;
            }
            eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.OpenInWebView;
            return false;
        }

        // It's a host URL, so we have to re-route it to the local one
        var localUri = HostToAbsoluteLocalUri(uri);
        MainThreadExt.InvokeLater(() => _ = NavigateTo(localUri, !wasOnLocalUri));
        eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
        return false;
    }

    private static bool IsAllowedHostUri(Uri uri)
    {
        if (MauiSettings.WebAuth.UseSystemBrowser)
            return false;

        var pathAndQuery = uri.PathAndQuery.ToLowerInvariant();
        if (pathAndQuery.OrdinalStartsWith("/maui-auth/"))
            return true;
        if (pathAndQuery.OrdinalStartsWith("/signin"))
            return true;
        if (pathAndQuery.OrdinalStartsWith("/signout"))
            return true;
        if (pathAndQuery.OrdinalStartsWith("/fusion/close"))
            return true;
        return false;
    }

    private static string HostToAbsoluteLocalUri(Uri hostUri)
    {
        var relativeUri = MauiSettings.BaseUri.MakeRelativeUri(hostUri);
        return RelativeToAbsoluteLocalUri(relativeUri.ToString());
    }

    private static string RelativeToAbsoluteLocalUri(string relativeUri)
        => new Uri(BaseLocalUri, relativeUri).ToString();
}

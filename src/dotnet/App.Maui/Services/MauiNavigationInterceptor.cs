using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView;

namespace ActualChat.App.Maui.Services;

public class MauiNavigationInterceptor
{
    // ReSharper disable once CollectionNeverUpdated.Local
    private static readonly HashSet<string> AllowedExternalHosts = MauiSettings.WebAuth.UseSystemBrowser
        ? new(StringComparer.Ordinal)
        : new(StringComparer.Ordinal) { "accounts.google.com", "appleid.apple.com" };
    private static readonly Uri BaseLocalUri = new($"https://{MauiSettings.LocalHost}/");

    private CancellationTokenSource? _cancelNavigationCts;
    private Uri _lastLocalUri = BaseLocalUri;
    private bool _isOnLocalUri = true;
    private ILogger? _log;

    private IServiceProvider Services { get; } // This is root IServiceProvider!
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public MauiNavigationInterceptor(IServiceProvider services)
        => Services = services;

    public void TryIntercept(Uri uri, UrlLoadingEventArgs eventArgs)
    {
        var wasOnLocalUri = _isOnLocalUri;
        _cancelNavigationCts.CancelAndDisposeSilently();
        if (OrdinalEquals(uri.Host, MauiSettings.LocalHost)) {
            // Local MAUI app URL
            _lastLocalUri = uri;
            _isOnLocalUri = true;
            eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.OpenInWebView;
            return;
        }

        _isOnLocalUri = false;
        if (!MauiSettings.BaseUri.IsBaseOf(uri)) {
            // Neither local MAUI app URL nor host URL
            eventArgs.UrlLoadingStrategy = AllowedExternalHosts.Contains(uri.Host)
                ? UrlLoadingStrategy.OpenInWebView
                : UrlLoadingStrategy.OpenExternally;
            return;
        }

        // If we're here, it's a host URL
        if (IsAllowedHostUri(uri)) {
            if (uri.PathAndQuery.OrdinalIgnoreCaseStartsWith("/fusion/close"))
                _ = NavigateTo(_lastLocalUri.ToString(), true);
            eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.OpenInWebView;
            return;
        }

        // It's a host URL, so we have to re-route it to the local one
        var localUri = HostToAbsoluteLocalUri(uri);
        _ = NavigateTo(localUri, !wasOnLocalUri);
        eventArgs.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
    }

    // Private methods

    private bool IsAllowedHostUri(Uri uri)
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

    private string HostToAbsoluteLocalUri(Uri hostUri)
    {
        var relativeUri = MauiSettings.BaseUri.MakeRelativeUri(hostUri);
        return RelativeToAbsoluteLocalUri(relativeUri.ToString());
    }

    private string RelativeToAbsoluteLocalUri(string relativeUri)
        => new Uri(BaseLocalUri, relativeUri).ToString();

    private async Task NavigateTo(string uri, bool mustReload)
    {
        var cts = _cancelNavigationCts = new();
        var cancellationToken = cts.Token;
        while (true) {
            try {
                // MainPage.Current!.NavigateTo(uri);
                // break;
                var services = await ScopedServicesTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                var blazorCircuitContext = services.GetRequiredService<AppBlazorCircuitContext>();
                await blazorCircuitContext.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
                await blazorCircuitContext.Dispatcher.InvokeAsync(async () => {
                    var history = services.GetRequiredService<History>();
                    if (mustReload)
                        await history.ForceReload("return to MAUI app", uri).ConfigureAwait(false);
                    else
                        history.Nav.NavigateTo(uri);
                }).ConfigureAwait(false);
                break;
            }
            catch (Exception e) {
                if (cancellationToken.IsCancellationRequested)
                    break;

                Log.LogError(e, "NavigateTo failed, retrying...");
            }
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
    }
}
